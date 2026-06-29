# Resource Play Values

Generated: 2026-06-28T09:27:38.4550280+00:00
Decks: 32
Runs: 50
Samples/deck: 2
Max branch: 4

## Benchmark Summary

Kind: benchmark
Elapsed seconds: 237.132
Median deck seconds: 7.489
P75 deck seconds: 9.537
Slow deck threshold: 18.723
Top3 share: 0.171
Slow decks: 0
Selection note: 6/22/4 selection after replacing slow act2/final decks; branch5 benchmark timed out, branch4 fallback accepted if no extreme deck remains

| Deck | RunId | Group | Cards | Samples | Probes | Baseline s | Probe s | Total s | Slow |
| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 0 | 1781972583 | floor8 | 19 | 2 | 6 | 1.26 | 4.836 | 6.096 |  |
| 1 | 1781539954 | floor8 | 18 | 2 | 6 | 1.038 | 7.728 | 8.767 |  |
| 2 | 1781605206 | floor8 | 18 | 2 | 6 | 1.257 | 8.14 | 9.397 |  |
| 3 | 1781268790 | floor8 | 17 | 2 | 6 | 0.425 | 3.01 | 3.435 |  |
| 4 | 1782215867 | floor8 | 17 | 2 | 6 | 0.598 | 4.114 | 4.713 |  |
| 5 | 1782324134 | floor8 | 17 | 2 | 6 | 0.497 | 3.248 | 3.745 |  |
| 6 | 1781539954 | act2Start | 22 | 2 | 6 | 1.218 | 8.825 | 10.043 |  |
| 7 | 1781972583 | act2Start | 22 | 2 | 6 | 0.68 | 4.146 | 4.826 |  |
| 8 | 1781177006 | act2Start | 21 | 2 | 6 | 0.476 | 3.498 | 3.974 |  |
| 9 | 1781186789 | act2Start | 21 | 2 | 6 | 0.905 | 6.006 | 6.911 |  |
| 10 | 1781268790 | act2Start | 21 | 2 | 6 | 1.424 | 9.337 | 10.762 |  |
| 11 | 1781605206 | act2Start | 21 | 2 | 6 | 1.349 | 9.169 | 10.518 |  |
| 12 | 1781627314 | act2Start | 20 | 2 | 6 | 1.264 | 7.86 | 9.125 |  |
| 13 | 1782128935 | act2Start | 20 | 2 | 6 | 0.409 | 2.584 | 2.993 |  |
| 14 | 1782215867 | act2Start | 20 | 2 | 6 | 1.112 | 7.887 | 8.999 |  |
| 15 | 1781278635 | act2Start | 19 | 2 | 6 | 1.117 | 7.813 | 8.93 |  |
| 16 | 1781356211 | act2Start | 19 | 2 | 6 | 0.54 | 3.6 | 4.14 |  |
| 17 | 1781515844 | act2Start | 19 | 2 | 6 | 0.622 | 4.29 | 4.912 |  |
| 18 | 1781612675 | act2Start | 19 | 2 | 6 | 1.042 | 7.101 | 8.143 |  |
| 19 | 1781862463 | act2Start | 19 | 2 | 6 | 0.663 | 4.432 | 5.095 |  |
| 20 | 1782040779 | act2Start | 19 | 2 | 6 | 1.151 | 8.805 | 9.955 |  |
| 21 | 1782117795 | act2Start | 19 | 2 | 6 | 1.358 | 8.79 | 10.148 |  |
| 22 | 1782152408 | act2Start | 19 | 2 | 6 | 0.667 | 4.324 | 4.991 |  |
| 23 | 1782170737 | act2Start | 19 | 2 | 6 | 0.389 | 2.663 | 3.052 |  |
| 24 | 1781249842 | act2Start | 18 | 2 | 6 | 0.645 | 4.175 | 4.821 |  |
| 25 | 1781522257 | act2Start | 18 | 2 | 6 | 0.916 | 5.788 | 6.703 |  |
| 26 | 1781536590 | act2Start | 18 | 2 | 6 | 1.681 | 11.475 | 13.156 |  |
| 27 | 1781589239 | act2Start | 18 | 2 | 6 | 0.494 | 3.475 | 3.969 |  |
| 28 | 1782140611 | final | 30 | 2 | 6 | 1.62 | 11.371 | 12.991 |  |
| 29 | 1782145020 | final | 27 | 2 | 6 | 1.088 | 6.979 | 8.067 |  |
| 30 | 1781356211 | final | 22 | 2 | 6 | 1.525 | 12.914 | 14.439 |  |
| 31 | 1781268790 | final | 30 | 2 | 6 | 1.308 | 8.006 | 9.314 |  |

## Aggregates

| Resource | Horizon | Weighted value/play | Sample mean value/play | Expected delta sum | Run-scaled delta sum | Probe plays | Valid | Invalid |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| draw | shortline | 3.98 | 4.155 | 208.868 | 10443.4 | 2624 | 64 | 0 |
| draw | midline | 5.256 | 5.192 | 538.896 | 26944.8 | 5126 | 64 | 0 |
| draw | longline | 6.598 | 6.779 | 1120.042 | 56002.1 | 8488 | 64 | 0 |
| energyGain | shortline | 8.897 | 9.378 | 597.186 | 29859.3 | 3356 | 64 | 0 |
| energyGain | midline | 9.791 | 10.813 | 1314.141 | 65707.05 | 6711 | 64 | 0 |
| energyGain | longline | 10.875 | 12.582 | 2468.577 | 123428.85 | 11350 | 64 | 0 |
| starGain | shortline | 2.287 | 2.487 | 132.013 | 6600.65 | 2886 | 64 | 0 |
| starGain | midline | 5.491 | 6.32 | 631.829 | 31591.45 | 5753 | 64 | 0 |
| starGain | longline | 6.922 | 8.701 | 1341.162 | 67058.1 | 9687 | 64 | 0 |

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
| 24 | act2Start | 17 | 11 | ParticleWall | draw | 51 | 5.467 | 94 | 6.785 | 148 | 10.65 |
| 24 | act2Start | 17 | 11 | ParticleWall | energyGain | 51 | 9.139 | 100 | 10.255 | 142 | 15.321 |
| 24 | act2Start | 17 | 11 | ParticleWall | starGain | 51 | 2.317 | 105 | 4.276 | 175 | 2.872 |
| 24 | act2Start | 17 | 16 | SwordSage | draw | 50 | 3.413 | 50 | 10.771 | 50 | 23.784 |
| 24 | act2Start | 17 | 16 | SwordSage | energyGain | 50 | 15.727 | 50 | 18.579 | 50 | 35.081 |
| 24 | act2Start | 17 | 16 | SwordSage | starGain | 50 | 1.432 | 50 | 7.113 | 50 | 8.685 |
| 25 | act2Start | 17 | 0 | Alchemize+1 | draw | 35 | -10.239 | 35 | 2.22 | 35 | 8.583 |
| 25 | act2Start | 17 | 0 | Alchemize+1 | energyGain | 35 | 12.164 | 35 | 5.603 | 35 | 2.714 |
| 25 | act2Start | 17 | 0 | Alchemize+1 | starGain | 35 | 3.676 | 35 | 10.961 | 35 | 5.874 |
| 25 | act2Start | 17 | 6 | DefendRegent | draw | 45 | 9.173 | 97 | 9.028 | 158 | 3.093 |
| 25 | act2Start | 17 | 6 | DefendRegent | energyGain | 57 | 5.596 | 117 | 4.774 | 190 | 4.122 |
| 25 | act2Start | 17 | 6 | DefendRegent | starGain | 61 | -1.228 | 112 | 6.353 | 183 | 7.973 |
| 26 | act2Start | 17 | 5 | DefendRegent | draw | 49 | 7.053 | 92 | 12.997 | 146 | 12.672 |
| 26 | act2Start | 17 | 5 | DefendRegent | energyGain | 71 | 6.025 | 126 | 5.3 | 211 | 5.603 |
| 26 | act2Start | 17 | 5 | DefendRegent | starGain | 62 | -1.052 | 124 | -0.274 | 192 | 2.747 |
| 26 | act2Start | 17 | 8 | Glow+1 | draw | 67 | 8.405 | 126 | 10.751 | 205 | 10.304 |
| 26 | act2Start | 17 | 8 | Glow+1 | energyGain | 62 | 6.436 | 125 | 5.58 | 208 | 5.875 |
| 26 | act2Start | 17 | 8 | Glow+1 | starGain | 62 | -0.423 | 123 | -0.309 | 197 | -0.513 |
| 27 | act2Start | 17 | 14 | StrikeRegent | draw | 28 | 8.082 | 49 | 9.784 | 95 | 7.154 |
| 27 | act2Start | 17 | 14 | StrikeRegent | energyGain | 46 | 6.409 | 97 | 7.175 | 174 | 7.001 |
| 27 | act2Start | 17 | 14 | StrikeRegent | starGain | 37 | 3.245 | 68 | 10.14 | 113 | 10.4 |
| 27 | act2Start | 17 | 17 | Venerate+1 | draw | 51 | 3.754 | 106 | 6.293 | 182 | 7.834 |
| 27 | act2Start | 17 | 17 | Venerate+1 | energyGain | 52 | 9.514 | 114 | 12.127 | 192 | 16.795 |
| 27 | act2Start | 17 | 17 | Venerate+1 | starGain | 50 | 7.618 | 105 | 9.95 | 188 | 12.154 |
| 28 | final | 47 | 10 | DefendRegent | draw | 27 | 25.657 | 59 | 5.333 | 82 | 6.688 |
| 28 | final | 47 | 10 | DefendRegent | energyGain | 44 | 9.247 | 88 | 10.419 | 148 | 13.934 |
| 28 | final | 47 | 10 | DefendRegent | starGain | 32 | 5.552 | 60 | 12.467 | 98 | 10.771 |
| 28 | final | 47 | 17 | GuidingStar+1 | draw | 28 | 11.502 | 47 | -5.455 | 61 | 5.798 |
| 28 | final | 47 | 17 | GuidingStar+1 | energyGain | 36 | 23.778 | 66 | 17.886 | 86 | 16.692 |
| 28 | final | 47 | 17 | GuidingStar+1 | starGain | 28 | 8.075 | 52 | 15.475 | 74 | 16.807 |
| 29 | final | 47 | 11 | GammaBlast | draw | 32 | 10.536 | 46 | 3.759 | 58 | 0.184 |
| 29 | final | 47 | 11 | GammaBlast | energyGain | 32 | 18.945 | 49 | 17.492 | 78 | 16.571 |
| 29 | final | 47 | 11 | GammaBlast | starGain | 36 | 12.424 | 50 | 26.139 | 70 | 26.709 |
| 29 | final | 47 | 19 | PaleBlueDot+1 | draw | 31 | 7.131 | 50 | -0.761 | 50 | -13.888 |
| 29 | final | 47 | 19 | PaleBlueDot+1 | energyGain | 31 | 20.248 | 50 | 24.106 | 50 | 28.787 |
| 29 | final | 47 | 19 | PaleBlueDot+1 | starGain | 31 | 11.181 | 50 | 19.678 | 50 | 22.522 |
| 30 | final | 47 | 16 | KinglyPunch+1 | draw | 32 | 7.741 | 69 | 11.651 | 128 | 14.553 |
| 30 | final | 47 | 16 | KinglyPunch+1 | energyGain | 47 | 11.009 | 100 | 15.998 | 191 | 12.369 |
| 30 | final | 47 | 16 | KinglyPunch+1 | starGain | 26 | 10.35 | 65 | 15.612 | 110 | 4.799 |
| 30 | final | 47 | 21 | Venerate+1 | draw | 38 | 7.483 | 94 | 21.938 | 191 | 24.744 |
| 30 | final | 47 | 21 | Venerate+1 | energyGain | 48 | 20.218 | 107 | 35.415 | 207 | 35.538 |
| 30 | final | 47 | 21 | Venerate+1 | starGain | 32 | 8.363 | 79 | 13.654 | 152 | 23.144 |
| 31 | final | 47 | 2 | BulkUp+1 | draw | 38 | 10.375 | 50 | 19.416 | 50 | 4.987 |
| 31 | final | 47 | 2 | BulkUp+1 | energyGain | 39 | 22.615 | 50 | 33.27 | 50 | 19.936 |
| 31 | final | 47 | 2 | BulkUp+1 | starGain | 39 | 3.151 | 50 | 1.647 | 50 | 14.632 |
| 31 | final | 47 | 26 | StrikeRegent | draw | 14 | 7.682 | 37 | 19.105 | 61 | 12.455 |
| 31 | final | 47 | 26 | StrikeRegent | energyGain | 34 | 5.316 | 75 | 10.362 | 121 | 7.977 |
| 31 | final | 47 | 26 | StrikeRegent | starGain | 16 | -5.778 | 41 | 2.901 | 65 | 18.763 |
