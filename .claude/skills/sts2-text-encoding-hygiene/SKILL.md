---
name: sts2-text-encoding-hygiene
description: Normalize mojibake-prone text in this StS2 repo. Use when Codex sees or is asked to fix UTF-8/PowerShell display issues, mojibake symptoms, or Unicode punctuation/math symbols in docs, comments, CLI help, or overlay labels that should remain ASCII-safe.
---

# StS2 Text Encoding Hygiene

Use this skill when fixing encoding display problems or adding repo text that may
be read through Windows PowerShell 5.1, legacy ANSI/GBK terminals, or plain
`Get-Content`.

## Rules

- Keep docs, code comments, CLI help, and diagnostic labels ASCII where practical.
- Do not replace Chinese localization text or real game/user-facing translated
  content merely because it is non-ASCII.
- Prefer ASCII spellings for punctuation and math notation:
  `-`, `->`, `<-`, `<->`, `...`, `x`, `/`, `~`, `<=`, `>=`, `sum`, `dEV`.
- Replace progress-bar block glyphs in source strings with ASCII equivalents
  when terminal safety matters, for example `#` and `-`.
- Treat mojibake symptoms such as the UTF-8 bytes for smart punctuation being
  rendered as CJK-looking glyphs as display/encoding bugs: first inspect the
  intended UTF-8 character, then replace with an ASCII spelling.

## Workflow

1. Search for both mojibake literals and the intended Unicode symbols:

   ```powershell
   # Search by explicit code point with a short temporary script when needed.
   # Common targets: U+2014, U+2013, U+2192, U+2190, U+2194, U+2026,
   # U+00D7, U+00F7, U+2248, U+2264, U+2265, U+2212, U+03A3,
   # U+0394, U+201C, U+201D, U+2018, U+2019, U+2593, U+2591.
   ```

2. For bulk cleanup, use a temporary script that:
   - walks the repo from the root;
   - skips `.git`, build output, package output, and temporary/generated binary
     folders;
   - only edits UTF-8 text files without NUL bytes;
   - prints every changed path and replacement count.

3. Verify the specific reported line with ordinary `Get-Content`, not only a
   UTF-8-aware reader.

4. Run the relevant tests and `git diff --check` on the files changed by the
   cleanup. If unrelated generated files were already dirty, do not stage them
   unless the replacement script actually changed them and the user asked to
   include them.

## Validation

Use targeted checks before finishing:

```powershell
# Search by code point or run the cleanup script's dry-run mode.
git diff --check -- <changed-files>
```
