# Resource Play Values

Generated: 2026-06-28T09:02:35.6978619+00:00
Decks: 32
Runs: 50
Samples/deck: 2
Max branch: 4

## Benchmark Summary

Kind: benchmark
Elapsed seconds: 1209.702
Median deck seconds: 8.745
P75 deck seconds: 10.52
Slow deck threshold: 21.863
Top3 share: 0.79
Slow decks: 5
Selection note: initial 6/22/4 selection; branch5 benchmark timed out after 30 minutes, fallback benchmark uses branch4

| Deck | RunId | Group | Cards | Samples | Probes | Baseline s | Probe s | Total s | Slow |
| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 0 | 1781972583 | floor8 | 19 | 2 | 6 | 1.246 | 4.937 | 6.183 |  |
| 1 | 1781539954 | floor8 | 18 | 2 | 6 | 1.077 | 8.199 | 9.275 |  |
| 2 | 1781605206 | floor8 | 18 | 2 | 6 | 1.301 | 8.383 | 9.684 |  |
| 3 | 1781268790 | floor8 | 17 | 2 | 6 | 0.402 | 3.114 | 3.516 |  |
| 4 | 1782215867 | floor8 | 17 | 2 | 6 | 0.61 | 4.271 | 4.881 |  |
| 5 | 1782324134 | floor8 | 17 | 2 | 6 | 0.505 | 3.329 | 3.834 |  |
| 6 | 1781539954 | act2Start | 22 | 2 | 6 | 1.197 | 9.145 | 10.341 |  |
| 7 | 1781972583 | act2Start | 22 | 2 | 6 | 0.699 | 4.3 | 4.999 |  |
| 8 | 1781177006 | act2Start | 21 | 2 | 6 | 0.501 | 3.584 | 4.086 |  |
| 9 | 1781186789 | act2Start | 21 | 2 | 6 | 0.949 | 6.172 | 7.121 |  |
| 10 | 1781268790 | act2Start | 21 | 2 | 6 | 1.463 | 9.568 | 11.031 |  |
| 11 | 1781605206 | act2Start | 21 | 2 | 6 | 1.38 | 9.578 | 10.958 |  |
| 12 | 1781627314 | act2Start | 20 | 2 | 6 | 1.288 | 8.133 | 9.422 |  |
| 13 | 1782128935 | act2Start | 20 | 2 | 6 | 0.408 | 2.636 | 3.044 |  |
| 14 | 1782215867 | act2Start | 20 | 2 | 6 | 1.128 | 8.161 | 9.289 |  |
| 15 | 1781278635 | act2Start | 19 | 2 | 6 | 1.148 | 8.098 | 9.246 |  |
| 16 | 1781356211 | act2Start | 19 | 2 | 6 | 0.553 | 3.726 | 4.278 |  |
| 17 | 1781515844 | act2Start | 19 | 2 | 6 | 0.639 | 4.42 | 5.059 |  |
| 18 | 1781612675 | act2Start | 19 | 2 | 6 | 1.067 | 7.177 | 8.244 |  |
| 19 | 1781862463 | act2Start | 19 | 2 | 6 | 0.697 | 4.535 | 5.232 |  |
| 20 | 1782040779 | act2Start | 19 | 2 | 6 | 1.167 | 9.087 | 10.254 |  |
| 21 | 1782117795 | act2Start | 19 | 2 | 6 | 1.364 | 9.01 | 10.374 |  |
| 22 | 1782152408 | act2Start | 19 | 2 | 6 | 0.687 | 4.356 | 5.042 |  |
| 23 | 1782170737 | act2Start | 19 | 2 | 6 | 0.414 | 2.773 | 3.187 |  |
| 24 | 1781194308 | act2Start | 18 | 2 | 6 | 25.178 | 179.051 | 204.229 | totalSeconds 204.229 > slowDeckThreshold 21.863; top3 cumulative share 0.79 > 0.25 |
| 25 | 1781249842 | act2Start | 18 | 2 | 6 | 0.686 | 4.413 | 5.099 |  |
| 26 | 1781522257 | act2Start | 18 | 2 | 6 | 0.957 | 6.425 | 7.383 |  |
| 27 | 1781536590 | act2Start | 18 | 2 | 6 | 1.612 | 10.726 | 12.338 |  |
| 28 | 1781972583 | final | 26 | 2 | 6 | 4.405 | 29.861 | 34.266 | totalSeconds 34.266 > slowDeckThreshold 21.863 |
| 29 | 1781862463 | final | 24 | 2 | 6 | 14.621 | 109.336 | 123.957 | totalSeconds 123.957 > slowDeckThreshold 21.863; top3 cumulative share 0.79 > 0.25 |
| 30 | 1782152408 | final | 24 | 2 | 6 | 3.59 | 23.245 | 26.835 | totalSeconds 26.835 > slowDeckThreshold 21.863 |
| 31 | 1781692483 | final | 21 | 2 | 6 | 78.503 | 548.508 | 627.011 | totalSeconds 627.011 > slowDeckThreshold 21.863; top3 cumulative share 0.79 > 0.25 |

## Aggregates

| Resource | Horizon | Weighted value/play | Sample mean value/play | Expected delta sum | Run-scaled delta sum | Probe plays | Valid | Invalid |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| draw | shortline | 3.54 | 3.54 | 191.464 | 9573.2 | 2704 | 64 | 0 |
| draw | midline | 3.961 | 3.617 | 426.204 | 21310.2 | 5380 | 64 | 0 |
| draw | longline | 6.55 | 7.381 | 1181.149 | 59057.45 | 9017 | 64 | 0 |
| energyGain | shortline | 9.904 | 9.961 | 723.781 | 36189.05 | 3654 | 64 | 0 |
| energyGain | midline | 9.622 | 10.232 | 1416.594 | 70829.7 | 7361 | 64 | 0 |
| energyGain | longline | 10.953 | 11.804 | 2788.49 | 139424.5 | 12729 | 64 | 0 |
| starGain | shortline | 1.541 | 1.616 | 91.883 | 4594.15 | 2981 | 64 | 0 |
| starGain | midline | 4.472 | 5.209 | 542.013 | 27100.65 | 6060 | 64 | 0 |
| starGain | longline | 5.166 | 6.381 | 1065.55 | 53277.5 | 10313 | 64 | 0 |

## Probe Details

| Deck | Group | Layer | Copy | Card | Resource | Short plays | Short value/play | Mid plays | Mid value/play | Long plays | Long value/play |
| ---: | --- | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | floor8 | 8 | 2 | CloakOfStars | draw | 55 | 3.742 | 105 | 3.801 | 192 | 5.532 |
| 0 | floor8 | 8 | 2 | CloakOfStars | energyGain | 52 | 2.756 | 101 | 3.693 | 182 | 3.796 |
| 0 | floor8 | 8 | 2 | CloakOfStars | starGain | 54 | 0.902 | 108 | 0.702 | 189 | 0.995 |
| 0 | floor8 | 8 | 8 | GatherLight | draw | 53 | 3.85 | 110 | 4.295 | 172 | 3.776 |
| 0 | floor8 | 8 | 8 | GatherLight | energyGain | 52 | 6.419 | 113 | 5.904 | 195 | 7.454 |
| 0 | floor8 | 8 | 8 | GatherLight | starGain | 51 | 0.435 | 109 | 0.443 | 186 | 2.504 |
| 1 | floor8 | 8 | 7 | DefendRegent | draw | 52 | 5.72 | 106 | 4.859 | 188 | 3.981 |
| 1 | floor8 | 8 | 7 | DefendRegent | energyGain | 66 | 5.192 | 143 | 4.023 | 242 | 5.904 |
| 1 | floor8 | 8 | 7 | DefendRegent | starGain | 70 | -0.084 | 136 | 0.295 | 239 | 0.199 |
| 1 | floor8 | 8 | 8 | FallingStar | draw | 75 | 4.065 | 152 | 2.721 | 259 | 3.014 |
| 1 | floor8 | 8 | 8 | FallingStar | energyGain | 75 | 4.383 | 159 | 4.604 | 260 | 4.118 |
| 1 | floor8 | 8 | 8 | FallingStar | starGain | 74 | 0.422 | 159 | 0.889 | 259 | 0.516 |
| 2 | floor8 | 8 | 5 | DefendRegent | draw | 53 | 4.424 | 97 | 3.687 | 157 | 5.847 |
| 2 | floor8 | 8 | 5 | DefendRegent | energyGain | 63 | 7.483 | 123 | 7.545 | 189 | 6.444 |
| 2 | floor8 | 8 | 5 | DefendRegent | starGain | 64 | 6.059 | 115 | 9.258 | 177 | 10.034 |
| 2 | floor8 | 8 | 16 | StrikeRegent | draw | 38 | 4.474 | 56 | 6.569 | 99 | 5.204 |
| 2 | floor8 | 8 | 16 | StrikeRegent | energyGain | 57 | 5.117 | 123 | 4.643 | 196 | 6.755 |
| 2 | floor8 | 8 | 16 | StrikeRegent | starGain | 54 | 5.305 | 107 | 9.952 | 175 | 9.033 |
| 3 | floor8 | 8 | 3 | CosmicIndifference | draw | 125 | 2.604 | 309 | 3.167 | 590 | 2.738 |
| 3 | floor8 | 8 | 3 | CosmicIndifference | energyGain | 144 | 5.222 | 344 | 4.527 | 644 | 4.667 |
| 3 | floor8 | 8 | 3 | CosmicIndifference | starGain | 123 | 2.843 | 311 | 6.174 | 607 | 7.304 |
| 3 | floor8 | 8 | 6 | DefendRegent | draw | 43 | -0.167 | 100 | 1.789 | 177 | 1.627 |
| 3 | floor8 | 8 | 6 | DefendRegent | energyGain | 49 | 5.041 | 95 | 5.658 | 170 | 6.376 |
| 3 | floor8 | 8 | 6 | DefendRegent | starGain | 56 | 2.062 | 106 | 6.755 | 178 | 9.153 |
| 4 | floor8 | 8 | 1 | Charge | draw | 15 | 6.01 | 24 | 9.877 | 47 | 15.183 |
| 4 | floor8 | 8 | 1 | Charge | energyGain | 53 | 15.792 | 110 | 18.704 | 204 | 21.667 |
| 4 | floor8 | 8 | 1 | Charge | starGain | 12 | 5.196 | 18 | 18.853 | 29 | 18.157 |
| 4 | floor8 | 8 | 7 | DefendRegent | draw | 40 | 4.855 | 63 | 1.317 | 113 | 3.412 |
| 4 | floor8 | 8 | 7 | DefendRegent | energyGain | 52 | 5.133 | 96 | 6.312 | 170 | 8.369 |
| 4 | floor8 | 8 | 7 | DefendRegent | starGain | 51 | 0.475 | 94 | 5.121 | 158 | 6.032 |
| 5 | floor8 | 8 | 2 | ChildOfTheStars | draw | 50 | 4.82 | 50 | 6.91 | 50 | 12.479 |
| 5 | floor8 | 8 | 2 | ChildOfTheStars | energyGain | 50 | 12.713 | 50 | 17.276 | 50 | 23.898 |
| 5 | floor8 | 8 | 2 | ChildOfTheStars | starGain | 50 | 3.526 | 50 | 16.699 | 50 | 24.379 |
| 5 | floor8 | 8 | 5 | DefendRegent | draw | 51 | 0.555 | 98 | 0.521 | 155 | 2.595 |
| 5 | floor8 | 8 | 5 | DefendRegent | energyGain | 82 | 8.86 | 164 | 10.557 | 282 | 11.34 |
| 5 | floor8 | 8 | 5 | DefendRegent | starGain | 59 | 2.675 | 123 | 6.796 | 221 | 8.406 |
| 6 | act2Start | 17 | 20 | StrikeRegent | draw | 34 | 0.963 | 79 | 7.087 | 149 | 6.086 |
| 6 | act2Start | 17 | 20 | StrikeRegent | energyGain | 55 | 11.105 | 121 | 5.399 | 208 | 6.878 |
| 6 | act2Start | 17 | 20 | StrikeRegent | starGain | 48 | 4.692 | 108 | 5.875 | 190 | 6.342 |
| 6 | act2Start | 17 | 21 | Venerate | draw | 52 | 6.213 | 114 | 2.637 | 188 | 4.201 |
| 6 | act2Start | 17 | 21 | Venerate | energyGain | 60 | 6.557 | 118 | 8.533 | 200 | 8.076 |
| 6 | act2Start | 17 | 21 | Venerate | starGain | 50 | 3.037 | 109 | 8.666 | 189 | 5.794 |
| 7 | act2Start | 17 | 11 | HiddenCache+1 | draw | 45 | 7.666 | 85 | 2.806 | 125 | -0.174 |
| 7 | act2Start | 17 | 11 | HiddenCache+1 | energyGain | 47 | 8.124 | 86 | 13.217 | 131 | 8.746 |
| 7 | act2Start | 17 | 11 | HiddenCache+1 | starGain | 46 | 0.567 | 88 | 1.259 | 130 | 0.624 |
| 7 | act2Start | 17 | 18 | StrikeRegent | draw | 18 | 4.15 | 41 | 4.424 | 70 | -6.596 |
| 7 | act2Start | 17 | 18 | StrikeRegent | energyGain | 46 | 7.799 | 84 | 5.866 | 129 | 5.435 |
| 7 | act2Start | 17 | 18 | StrikeRegent | starGain | 30 | 0.79 | 57 | 4.002 | 100 | -0.078 |
| 8 | act2Start | 17 | 2 | Charge | draw | 6 | 1.767 | 11 | -2.959 | 25 | 9.61 |
| 8 | act2Start | 17 | 2 | Charge | energyGain | 45 | 18.452 | 94 | 18.715 | 170 | 26.627 |
| 8 | act2Start | 17 | 2 | Charge | starGain | 4 | -10.913 | 7 | -15.3 | 14 | -4.589 |
| 8 | act2Start | 17 | 18 | StrikeRegent | draw | 17 | 1.747 | 36 | -2.307 | 60 | -8.533 |
| 8 | act2Start | 17 | 18 | StrikeRegent | energyGain | 42 | 5.686 | 82 | 6.305 | 151 | 8.19 |
| 8 | act2Start | 17 | 18 | StrikeRegent | starGain | 31 | -2.344 | 59 | -4.646 | 102 | -0.814 |
| 9 | act2Start | 17 | 16 | StrikeRegent | draw | 36 | 2.797 | 63 | 6.362 | 90 | 1.701 |
| 9 | act2Start | 17 | 16 | StrikeRegent | energyGain | 46 | 5.302 | 100 | 5.491 | 170 | 6.188 |
| 9 | act2Start | 17 | 16 | StrikeRegent | starGain | 40 | 4.863 | 93 | 6.93 | 149 | 7.138 |
| 9 | act2Start | 17 | 20 | Venerate | draw | 41 | -0.222 | 89 | 4.913 | 127 | 7.648 |
| 9 | act2Start | 17 | 20 | Venerate | energyGain | 46 | 9.108 | 93 | 10.845 | 147 | 12.05 |
| 9 | act2Start | 17 | 20 | Venerate | starGain | 44 | 0.103 | 88 | 6.874 | 143 | 9.182 |
| 10 | act2Start | 17 | 12 | Havoc+1 | draw | 51 | 3.688 | 97 | 5.423 | 164 | 9.182 |
| 10 | act2Start | 17 | 12 | Havoc+1 | energyGain | 47 | 7.104 | 92 | 6.528 | 154 | 8.343 |
| 10 | act2Start | 17 | 12 | Havoc+1 | starGain | 47 | 3.22 | 92 | 3.495 | 159 | 5.028 |
| 10 | act2Start | 17 | 20 | Venerate | draw | 31 | 3.665 | 63 | 4.671 | 109 | 2.695 |
| 10 | act2Start | 17 | 20 | Venerate | energyGain | 46 | 6.616 | 91 | 5.054 | 161 | 6.507 |
| 10 | act2Start | 17 | 20 | Venerate | starGain | 37 | -2.1 | 70 | 1.264 | 119 | 1.753 |
| 11 | act2Start | 17 | 6 | DefendRegent | draw | 45 | 6.158 | 88 | 6.232 | 132 | 9.888 |
| 11 | act2Start | 17 | 6 | DefendRegent | energyGain | 56 | 7.205 | 109 | 6.362 | 182 | 10.975 |
| 11 | act2Start | 17 | 6 | DefendRegent | starGain | 48 | 3.542 | 96 | 10.428 | 155 | 14.805 |
| 11 | act2Start | 17 | 9 | FallingStar | draw | 41 | 1.504 | 85 | 7.932 | 125 | 9.644 |
| 11 | act2Start | 17 | 9 | FallingStar | energyGain | 43 | 8.826 | 80 | 14.581 | 120 | 14.162 |
| 11 | act2Start | 17 | 9 | FallingStar | starGain | 44 | 2.195 | 83 | 10.322 | 130 | 14.23 |
| 12 | act2Start | 17 | 5 | DefendRegent | draw | 50 | -0.847 | 90 | 1.548 | 155 | 3.232 |
| 12 | act2Start | 17 | 5 | DefendRegent | energyGain | 54 | 7.517 | 112 | 7.79 | 204 | 10.738 |
| 12 | act2Start | 17 | 5 | DefendRegent | starGain | 53 | 2.29 | 108 | 7.746 | 197 | 9.561 |
| 12 | act2Start | 17 | 15 | StrikeRegent | draw | 42 | -2.126 | 82 | 4.256 | 150 | 4.916 |
| 12 | act2Start | 17 | 15 | StrikeRegent | energyGain | 53 | 5.521 | 121 | 6.457 | 210 | 9.85 |
| 12 | act2Start | 17 | 15 | StrikeRegent | starGain | 50 | 0.223 | 118 | 6.323 | 192 | 9.657 |
| 13 | act2Start | 17 | 1 | Bulwark | draw | 45 | 3.073 | 87 | 10.857 | 154 | 9.935 |
| 13 | act2Start | 17 | 1 | Bulwark | energyGain | 50 | 11.113 | 101 | 14.441 | 181 | 14.303 |
| 13 | act2Start | 17 | 1 | Bulwark | starGain | 44 | 6.366 | 80 | 6.627 | 144 | 4.514 |
| 13 | act2Start | 17 | 15 | StrikeRegent | draw | 35 | 5.457 | 62 | 5.065 | 120 | 1.387 |
| 13 | act2Start | 17 | 15 | StrikeRegent | energyGain | 50 | 5.596 | 102 | 8.24 | 187 | 8.117 |
| 13 | act2Start | 17 | 15 | StrikeRegent | starGain | 42 | 2.919 | 89 | 2.819 | 162 | 3.001 |
| 14 | act2Start | 17 | 4 | CosmicIndifference | draw | 93 | 6.555 | 208 | 7.746 | 377 | 8.352 |
| 14 | act2Start | 17 | 4 | CosmicIndifference | energyGain | 89 | 8.038 | 216 | 6.54 | 371 | 7.595 |
| 14 | act2Start | 17 | 4 | CosmicIndifference | starGain | 87 | 2.406 | 212 | 5.425 | 386 | 4.767 |
| 14 | act2Start | 17 | 5 | DefendRegent | draw | 38 | 10.279 | 81 | 11.187 | 142 | 11.334 |
| 14 | act2Start | 17 | 5 | DefendRegent | energyGain | 39 | 8.86 | 89 | 8.214 | 160 | 4.917 |
| 14 | act2Start | 17 | 5 | DefendRegent | starGain | 38 | 1.053 | 85 | 6.638 | 145 | 9.056 |
| 15 | act2Start | 17 | 11 | Hegemony+1 | draw | 36 | 1.801 | 57 | 8.7 | 70 | 10.694 |
| 15 | act2Start | 17 | 11 | Hegemony+1 | energyGain | 51 | 11.434 | 99 | 14.112 | 149 | 18.624 |
| 15 | act2Start | 17 | 11 | Hegemony+1 | starGain | 37 | 5.386 | 63 | 8.346 | 100 | 15.469 |
| 15 | act2Start | 17 | 13 | Quasar+1 | draw | 47 | -1.449 | 94 | 4.831 | 159 | 5.283 |
| 15 | act2Start | 17 | 13 | Quasar+1 | energyGain | 49 | 6.516 | 102 | 8.052 | 160 | 10.899 |
| 15 | act2Start | 17 | 13 | Quasar+1 | starGain | 50 | 4.117 | 96 | 4.426 | 159 | 5.353 |
| 16 | act2Start | 17 | 7 | DefendRegent | draw | 49 | 5.896 | 94 | 2.037 | 175 | 2.478 |
| 16 | act2Start | 17 | 7 | DefendRegent | energyGain | 47 | 4.586 | 105 | 4.764 | 201 | 4.825 |
| 16 | act2Start | 17 | 7 | DefendRegent | starGain | 49 | 2.687 | 114 | 2.7 | 224 | 3.088 |
| 16 | act2Start | 17 | 12 | KinglyPunch+1 | draw | 45 | 2.652 | 113 | 1.557 | 226 | 3.106 |
| 16 | act2Start | 17 | 12 | KinglyPunch+1 | energyGain | 40 | 5.714 | 99 | 4.369 | 206 | 5.507 |
| 16 | act2Start | 17 | 12 | KinglyPunch+1 | starGain | 47 | -0.034 | 113 | 2.042 | 236 | 4.275 |
| 17 | act2Start | 17 | 3 | DefendRegent | draw | 48 | 2.194 | 91 | 1.479 | 160 | 1.423 |
| 17 | act2Start | 17 | 3 | DefendRegent | energyGain | 66 | 10.347 | 125 | 11.848 | 210 | 13.605 |
| 17 | act2Start | 17 | 3 | DefendRegent | starGain | 53 | 4.226 | 110 | 6.3 | 183 | 6.595 |
| 17 | act2Start | 17 | 18 | WroughtInWar+1 | draw | 50 | 4.635 | 89 | 2.822 | 139 | 2.065 |
| 17 | act2Start | 17 | 18 | WroughtInWar+1 | energyGain | 57 | 12.681 | 122 | 13.94 | 204 | 17.263 |
| 17 | act2Start | 17 | 18 | WroughtInWar+1 | starGain | 48 | 3.032 | 89 | 8.962 | 150 | 10.549 |
| 18 | act2Start | 17 | 2 | Charge | draw | 15 | 17.767 | 43 | 10.856 | 72 | 26.161 |
| 18 | act2Start | 17 | 2 | Charge | energyGain | 53 | 25.961 | 111 | 19.805 | 218 | 20.542 |
| 18 | act2Start | 17 | 2 | Charge | starGain | 9 | 4.978 | 36 | 21.219 | 61 | 33.495 |
| 18 | act2Start | 17 | 14 | StrikeRegent | draw | 32 | -2.677 | 68 | -9.071 | 104 | -6.685 |
| 18 | act2Start | 17 | 14 | StrikeRegent | energyGain | 57 | 7.931 | 95 | 17.476 | 156 | 10.415 |
| 18 | act2Start | 17 | 14 | StrikeRegent | starGain | 29 | -4.971 | 59 | 4.474 | 108 | 3.06 |
| 19 | act2Start | 17 | 3 | DecisionsDecisions | draw | 4 | 11.063 | 9 | 12.578 | 13 | 9.027 |
| 19 | act2Start | 17 | 3 | DecisionsDecisions | energyGain | 7 | 13.521 | 20 | 5 | 29 | 11.352 |
| 19 | act2Start | 17 | 3 | DecisionsDecisions | starGain | 8 | 11.663 | 17 | 0.129 | 26 | 10.969 |
| 19 | act2Start | 17 | 15 | StrikeRegent | draw | 29 | -0.298 | 51 | 4.933 | 86 | 5.051 |
| 19 | act2Start | 17 | 15 | StrikeRegent | energyGain | 73 | 6.619 | 161 | 6.684 | 300 | 6.307 |
| 19 | act2Start | 17 | 15 | StrikeRegent | starGain | 49 | 0.184 | 90 | -1.362 | 149 | 2.638 |
| 20 | act2Start | 17 | 14 | ManifestAuthority+1 | draw | 58 | 2.604 | 118 | 2.523 | 191 | 7.592 |
| 20 | act2Start | 17 | 14 | ManifestAuthority+1 | energyGain | 57 | 8.547 | 114 | 15.663 | 192 | 20.871 |
| 20 | act2Start | 17 | 14 | ManifestAuthority+1 | starGain | 60 | 0.101 | 117 | 0.388 | 194 | 3.824 |
| 20 | act2Start | 17 | 16 | StrikeRegent | draw | 33 | -2.179 | 70 | 10.856 | 116 | 17.299 |
| 20 | act2Start | 17 | 16 | StrikeRegent | energyGain | 57 | 9.409 | 111 | 13.897 | 179 | 14.46 |
| 20 | act2Start | 17 | 16 | StrikeRegent | starGain | 49 | -2.632 | 99 | -1.247 | 162 | 2.962 |
| 21 | act2Start | 17 | 8 | FallingStar | draw | 53 | 0.475 | 117 | 6.208 | 221 | 24.456 |
| 21 | act2Start | 17 | 8 | FallingStar | energyGain | 57 | 6.43 | 114 | 8.375 | 207 | 19.609 |
| 21 | act2Start | 17 | 8 | FallingStar | starGain | 55 | 3.078 | 116 | 1.473 | 208 | 16.089 |
| 21 | act2Start | 17 | 17 | TheSmith+1 | draw | 13 | -7.138 | 30 | -12.877 | 48 | 43.423 |
| 21 | act2Start | 17 | 17 | TheSmith+1 | energyGain | 29 | 5.89 | 45 | 34.42 | 62 | 73.973 |
| 21 | act2Start | 17 | 17 | TheSmith+1 | starGain | 18 | 12.983 | 31 | 31.635 | 53 | 55.681 |
| 22 | act2Start | 17 | 15 | StrikeRegent | draw | 22 | -1.966 | 48 | 1.234 | 85 | -1.758 |
| 22 | act2Start | 17 | 15 | StrikeRegent | energyGain | 52 | 5.289 | 101 | 4.489 | 170 | 5.01 |
| 22 | act2Start | 17 | 15 | StrikeRegent | starGain | 40 | -1.863 | 80 | 1.348 | 144 | 4.047 |
| 22 | act2Start | 17 | 16 | StrikeRegent | draw | 26 | -0.233 | 58 | 1.089 | 85 | -1.959 |
| 22 | act2Start | 17 | 16 | StrikeRegent | energyGain | 54 | 6.148 | 102 | 5.884 | 167 | 5.529 |
| 22 | act2Start | 17 | 16 | StrikeRegent | starGain | 42 | -1.604 | 87 | 0.855 | 154 | 3.176 |
| 23 | act2Start | 17 | 1 | Bulwark+1 | draw | 54 | 0.009 | 106 | 2.572 | 130 | 4.068 |
| 23 | act2Start | 17 | 1 | Bulwark+1 | energyGain | 59 | 12.477 | 128 | 10.858 | 229 | 5.293 |
| 23 | act2Start | 17 | 1 | Bulwark+1 | starGain | 53 | 0.227 | 97 | 4.442 | 134 | 8.799 |
| 23 | act2Start | 17 | 15 | StrikeRegent | draw | 24 | 2.108 | 38 | -1.043 | 47 | -7.351 |
| 23 | act2Start | 17 | 15 | StrikeRegent | energyGain | 47 | 5.676 | 96 | 6.057 | 158 | 8.353 |
| 23 | act2Start | 17 | 15 | StrikeRegent | starGain | 41 | -4.04 | 92 | -4.11 | 166 | -18.224 |
| 24 | act2Start | 17 | 11 | HiddenCache+1 | draw | 76 | 3.317 | 160 | 4.547 | 322 | 9.644 |
| 24 | act2Start | 17 | 11 | HiddenCache+1 | energyGain | 82 | 6.971 | 179 | 8.408 | 339 | 14.565 |
| 24 | act2Start | 17 | 11 | HiddenCache+1 | starGain | 72 | -0.69 | 164 | -1.302 | 296 | -6.058 |
| 24 | act2Start | 17 | 16 | Venerate+1 | draw | 61 | 0.063 | 139 | 7.049 | 283 | 12.599 |
| 24 | act2Start | 17 | 16 | Venerate+1 | energyGain | 78 | 3.39 | 168 | 6.943 | 320 | 5.785 |
| 24 | act2Start | 17 | 16 | Venerate+1 | starGain | 55 | 1.557 | 123 | -0.71 | 245 | -4.543 |
| 25 | act2Start | 17 | 1 | Charge+1 | draw | 20 | 19.253 | 38 | 21.297 | 59 | 15.753 |
| 25 | act2Start | 17 | 1 | Charge+1 | energyGain | 57 | 22.548 | 108 | 18.249 | 211 | 16.496 |
| 25 | act2Start | 17 | 1 | Charge+1 | starGain | 11 | 13.623 | 24 | 11.104 | 48 | -5.397 |
| 25 | act2Start | 17 | 6 | DefendRegent | draw | 29 | 9.134 | 63 | 8.948 | 107 | 0.534 |
| 25 | act2Start | 17 | 6 | DefendRegent | energyGain | 52 | 9.746 | 106 | 9.904 | 178 | 12.205 |
| 25 | act2Start | 17 | 6 | DefendRegent | starGain | 33 | 2.78 | 70 | 14.313 | 121 | 8.771 |
| 26 | act2Start | 17 | 5 | DefendRegent | draw | 53 | 11.302 | 98 | 4.076 | 155 | 2.752 |
| 26 | act2Start | 17 | 5 | DefendRegent | energyGain | 59 | 9.406 | 104 | 1.961 | 175 | 2.575 |
| 26 | act2Start | 17 | 5 | DefendRegent | starGain | 54 | 0.147 | 110 | 7.931 | 177 | 3.643 |
| 26 | act2Start | 17 | 8 | DefendRegent | draw | 46 | 11.467 | 95 | -0.544 | 160 | 2.504 |
| 26 | act2Start | 17 | 8 | DefendRegent | energyGain | 64 | 8.456 | 114 | 6.812 | 194 | 4.263 |
| 26 | act2Start | 17 | 8 | DefendRegent | starGain | 58 | 0.951 | 111 | 6.215 | 184 | 5.207 |
| 27 | act2Start | 17 | 14 | StrikeRegent | draw | 35 | 11.764 | 74 | 15.661 | 106 | 13.844 |
| 27 | act2Start | 17 | 14 | StrikeRegent | energyGain | 69 | 6.47 | 126 | 5.484 | 227 | 7.013 |
| 27 | act2Start | 17 | 14 | StrikeRegent | starGain | 59 | -0.906 | 118 | -2.214 | 195 | -1.848 |
| 27 | act2Start | 17 | 17 | Venerate | draw | 61 | 5.183 | 120 | 11.845 | 188 | 12.138 |
| 27 | act2Start | 17 | 17 | Venerate | energyGain | 63 | 4.147 | 128 | 4.02 | 220 | 5.347 |
| 27 | act2Start | 17 | 17 | Venerate | starGain | 58 | -0.361 | 117 | -1.348 | 194 | -1.16 |
| 28 | final | 47 | 5 | DefendRegent | draw | 26 | 2.613 | 47 | -7.285 | 71 | 3.505 |
| 28 | final | 47 | 5 | DefendRegent | energyGain | 43 | 19.112 | 76 | 18.311 | 109 | 14.649 |
| 28 | final | 47 | 5 | DefendRegent | starGain | 30 | 6.823 | 56 | -3.701 | 80 | 8.926 |
| 28 | final | 47 | 6 | DefendRegent | draw | 21 | -11.181 | 42 | -17.995 | 64 | -3.874 |
| 28 | final | 47 | 6 | DefendRegent | energyGain | 40 | 18.391 | 74 | 16.882 | 109 | 14.589 |
| 28 | final | 47 | 6 | DefendRegent | starGain | 21 | 2.46 | 51 | -6.272 | 78 | -16.056 |
| 29 | final | 47 | 6 | DecisionsDecisions+1 | draw | 18 | 0.889 | 27 | -3.511 | 38 | 17.775 |
| 29 | final | 47 | 6 | DecisionsDecisions+1 | energyGain | 22 | 7.155 | 36 | 22.399 | 45 | 17.738 |
| 29 | final | 47 | 6 | DecisionsDecisions+1 | starGain | 20 | 1.243 | 31 | 7.5 | 41 | 13.877 |
| 29 | final | 47 | 12 | Glimmer+1 | draw | 80 | 2.489 | 171 | 7.094 | 297 | 5.6 |
| 29 | final | 47 | 12 | Glimmer+1 | energyGain | 147 | 11.687 | 347 | 14.818 | 695 | 16.174 |
| 29 | final | 47 | 12 | Glimmer+1 | starGain | 112 | 7.497 | 232 | 6.547 | 403 | 10.919 |
| 30 | final | 47 | 2 | BundleOfJoy | draw | 31 | 7.166 | 44 | 20.235 | 50 | 25.277 |
| 30 | final | 47 | 2 | BundleOfJoy | energyGain | 43 | 35.808 | 49 | 32.226 | 50 | 35.522 |
| 30 | final | 47 | 2 | BundleOfJoy | starGain | 30 | -4.69 | 46 | 23.811 | 49 | 24.265 |
| 30 | final | 47 | 6 | DefendRegent | draw | 25 | 2.102 | 46 | 16.534 | 60 | 23.866 |
| 30 | final | 47 | 6 | DefendRegent | energyGain | 45 | 14.366 | 89 | 23.386 | 137 | 33.531 |
| 30 | final | 47 | 6 | DefendRegent | starGain | 24 | -0.59 | 47 | 9.998 | 57 | 11.494 |
| 31 | final | 47 | 4 | CosmicIndifference | draw | 64 | 4.861 | 141 | -5.438 | 219 | 8.364 |
| 31 | final | 47 | 4 | CosmicIndifference | energyGain | 119 | 26.641 | 245 | 14.216 | 453 | 19.29 |
| 31 | final | 47 | 4 | CosmicIndifference | starGain | 56 | 2.24 | 126 | -4.352 | 210 | -3.625 |
| 31 | final | 47 | 15 | PillarOfCreation+1 | draw | 50 | 3.453 | 50 | -39.673 | 50 | 27.859 |
| 31 | final | 47 | 15 | PillarOfCreation+1 | energyGain | 50 | 35.39 | 50 | -6.91 | 50 | -25.221 |
| 31 | final | 47 | 15 | PillarOfCreation+1 | starGain | 50 | -18.931 | 50 | 17.16 | 50 | -8.561 |
