#!/usr/bin/env bash
# Runs LOCALLY (Git Bash on liao-work) from the repo root. Provisions a
# c7a.16xlarge Spot box, wires up S3 + IAM + SSH, then bootstraps it and uploads
# the one git-ignored input (card_facts.generated.json). End-to-end: after this,
# SSH in and run run-collection.sh.
#
# Prereqs: awscli v2 configured (`aws configure`), an existing EC2 key pair whose
# .pem you hold, and a default VPC in the region (set SUBNET_ID to override).
#
# Required env: AWS_REGION, KEY_NAME, S3_BUCKET
# Optional env: TARGET_OS (ubuntu|al2023), INSTANCE_TYPE (c7a.16xlarge),
#               PEM (~/.ssh/$KEY_NAME.pem), VOLUME_GB (40), RUN_BOOTSTRAP (1),
#               SUBNET_ID, ROLE_NAME, SSH_USER (defaults per OS)
set -euo pipefail

: "${AWS_REGION:?set AWS_REGION (e.g. ap-northeast-1)}"
: "${KEY_NAME:?set KEY_NAME (an existing EC2 key pair name)}"
: "${S3_BUCKET:?set S3_BUCKET (globally-unique bucket name)}"
INSTANCE_TYPE="${INSTANCE_TYPE:-c7a.16xlarge}"
PEM="${PEM:-$HOME/.ssh/$KEY_NAME.pem}"
VOLUME_GB="${VOLUME_GB:-40}"
RUN_BOOTSTRAP="${RUN_BOOTSTRAP:-1}"
ROLE_NAME="${ROLE_NAME:-SearchPolicyEc2Role}"
SG_NAME="search-policy-ssh"
export AWS_PAGER=""
R=(--region "$AWS_REGION")

# OS selection: Ubuntu 24.04 or Amazon Linux 2023 (both fine for a .NET 8 CPU job).
# NB: named TARGET_OS, not OS — Windows sets OS=Windows_NT, which would collide.
TARGET_OS="${TARGET_OS:-ubuntu}"
case "$TARGET_OS" in
  ubuntu)
    DEFAULT_USER=ubuntu; ROOT_DEVICE=/dev/sda1
    # describe-images is more portable than the canonical SSM path (which varies by region).
    AMI_ID="${AMI_ID:-$(aws "${R[@]}" ec2 describe-images --owners 099720109477 \
      --filters 'Name=name,Values=ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-amd64-server-*' 'Name=state,Values=available' \
      --query 'sort_by(Images,&CreationDate)[-1].ImageId' --output text)}" ;;
  al2023)
    DEFAULT_USER=ec2-user; ROOT_DEVICE=/dev/xvda
    AMI_ID="${AMI_ID:-$(aws "${R[@]}" ssm get-parameter \
      --name /aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64 \
      --query 'Parameter.Value' --output text)}" ;;
  *) echo "unknown TARGET_OS=$TARGET_OS (use ubuntu or al2023)"; exit 1 ;;
esac
SSH_USER="${SSH_USER:-$DEFAULT_USER}"

echo "== S3 bucket =="
if ! aws "${R[@]}" s3api head-bucket --bucket "$S3_BUCKET" 2>/dev/null; then
  if [ "$AWS_REGION" = "us-east-1" ]; then
    aws "${R[@]}" s3api create-bucket --bucket "$S3_BUCKET"
  else
    aws "${R[@]}" s3api create-bucket --bucket "$S3_BUCKET" \
      --create-bucket-configuration LocationConstraint="$AWS_REGION"
  fi
fi

echo "== IAM role + instance profile (write-only to this bucket) =="
if ! aws iam get-role --role-name "$ROLE_NAME" >/dev/null 2>&1; then
  aws iam create-role --role-name "$ROLE_NAME" --assume-role-policy-document '{
    "Version":"2012-10-17",
    "Statement":[{"Effect":"Allow","Principal":{"Service":"ec2.amazonaws.com"},"Action":"sts:AssumeRole"}]}'
fi
aws iam put-role-policy --role-name "$ROLE_NAME" --policy-name s3-results --policy-document "{
  \"Version\":\"2012-10-17\",
  \"Statement\":[
    {\"Effect\":\"Allow\",\"Action\":[\"s3:PutObject\",\"s3:GetObject\",\"s3:ListBucket\"],
     \"Resource\":[\"arn:aws:s3:::$S3_BUCKET\",\"arn:aws:s3:::$S3_BUCKET/*\"]}]}"
if ! aws iam get-instance-profile --instance-profile-name "$ROLE_NAME" >/dev/null 2>&1; then
  aws iam create-instance-profile --instance-profile-name "$ROLE_NAME"
  aws iam add-role-to-instance-profile --instance-profile-name "$ROLE_NAME" --role-name "$ROLE_NAME"
  echo "waiting 15s for instance-profile propagation..."; sleep 15
fi

echo "== security group (SSH from your IP) =="
MYIP="$(curl -s https://checkip.amazonaws.com)/32"
VPC_ID="$(aws "${R[@]}" ec2 describe-vpcs --filters Name=isDefault,Values=true \
  --query 'Vpcs[0].VpcId' --output text)"
SG_ID="$(aws "${R[@]}" ec2 describe-security-groups --filters Name=group-name,Values=$SG_NAME \
  --query 'SecurityGroups[0].GroupId' --output text 2>/dev/null || true)"
if [ "$SG_ID" = "None" ] || [ -z "$SG_ID" ]; then
  SG_ID="$(aws "${R[@]}" ec2 create-security-group --group-name "$SG_NAME" \
    --description "SSH for search-policy collection" --vpc-id "$VPC_ID" --query GroupId --output text)"
fi
aws "${R[@]}" ec2 authorize-security-group-ingress --group-id "$SG_ID" \
  --protocol tcp --port 22 --cidr "$MYIP" 2>/dev/null || true
echo "SG=$SG_ID  ingress SSH from $MYIP"

[ -n "$AMI_ID" ] && [ "$AMI_ID" != "None" ] || { echo "failed to resolve AMI for $TARGET_OS"; exit 1; }
echo "== $TARGET_OS AMI=$AMI_ID  user=$SSH_USER root=$ROOT_DEVICE =="

echo "== launch $INSTANCE_TYPE Spot =="
SUBNET_ARG=()
[ -n "${SUBNET_ID:-}" ] && SUBNET_ARG=(--subnet-id "$SUBNET_ID")
INSTANCE_ID="$(aws "${R[@]}" ec2 run-instances \
  --image-id "$AMI_ID" --instance-type "$INSTANCE_TYPE" --key-name "$KEY_NAME" \
  --security-group-ids "$SG_ID" "${SUBNET_ARG[@]}" \
  --iam-instance-profile Name="$ROLE_NAME" \
  --instance-market-options 'MarketType=spot,SpotOptions={SpotInstanceType=one-time}' \
  --block-device-mappings "[{\"DeviceName\":\"$ROOT_DEVICE\",\"Ebs\":{\"VolumeSize\":$VOLUME_GB,\"VolumeType\":\"gp3\"}}]" \
  --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=search-policy-collect}]' \
  --query 'Instances[0].InstanceId' --output text)"
echo "INSTANCE_ID=$INSTANCE_ID  — waiting for running..."
aws "${R[@]}" ec2 wait instance-running --instance-ids "$INSTANCE_ID"
IP="$(aws "${R[@]}" ec2 describe-instances --instance-ids "$INSTANCE_ID" \
  --query 'Reservations[0].Instances[0].PublicIpAddress' --output text)"
echo "PUBLIC_IP=$IP"

if [ "$RUN_BOOTSTRAP" = "1" ]; then
  echo "== waiting for sshd, then bootstrap =="
  for _ in $(seq 1 30); do
    ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=5 -i "$PEM" "$SSH_USER"@"$IP" true 2>/dev/null && break
    sleep 10
  done
  scp -i "$PEM" ops/aws-search-policy/bootstrap.sh "$SSH_USER"@"$IP":~/bootstrap.sh
  ssh -i "$PEM" "$SSH_USER"@"$IP" 'bash ~/bootstrap.sh'
  echo "== upload card_facts.generated.json =="
  scp -i "$PEM" data/extracted/card_facts.generated.json \
    "$SSH_USER"@"$IP":~/StS2-mod/data/extracted/card_facts.generated.json
fi

cat <<EOF

Ready. Now:
  ssh -i $PEM $SSH_USER@$IP
  tmux new -s collect && cd ~/StS2-mod
  # canary first, then the full base (100k groups):
  WORKERS=60 S3_BUCKET=$S3_BUCKET RUN_ID=run-$(date +%Y%m%d) \\
    bash ops/aws-search-policy/run-collection.sh

Tear down when done:
  aws --region $AWS_REGION ec2 terminate-instances --instance-ids $INSTANCE_ID
EOF
