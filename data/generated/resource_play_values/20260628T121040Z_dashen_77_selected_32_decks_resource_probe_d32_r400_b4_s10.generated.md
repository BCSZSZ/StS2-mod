# Resource Play Values

Generated: 2026-06-28T12:10:40.1935782+00:00
Decks: 32
Runs: 400
Samples/deck: 10
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

## Run Timing Summary

Kind: formal
Elapsed seconds: 9767.807
Median deck seconds: 286.798
P75 deck seconds: 387.196
Slow deck threshold: 716.994
Top3 share: 0.218
Slow decks: 1
Selection note: 6/22/4 selected_32 after replacing slow benchmark decks; branch5 benchmark timed out after 30 minutes, formal uses fallback runs400 samples10 branch4; replacement benchmark passed with slowDecks=0

| Deck | RunId | Group | Cards | Samples | Probes | Baseline s | Probe s | Total s | Slow |
| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 0 | 1781972583 | floor8 | 19 | 10 | 30 | 6.275 | 184.387 | 190.663 |  |
| 1 | 1781539954 | floor8 | 18 | 10 | 30 | 8.343 | 304.752 | 313.095 |  |
| 2 | 1781605206 | floor8 | 18 | 10 | 30 | 10.829 | 375.839 | 386.668 |  |
| 3 | 1781268790 | floor8 | 17 | 10 | 30 | 2.991 | 106.605 | 109.595 |  |
| 4 | 1782215867 | floor8 | 17 | 10 | 30 | 5.123 | 172.292 | 177.414 |  |
| 5 | 1782324134 | floor8 | 17 | 10 | 30 | 4.002 | 142.394 | 146.397 |  |
| 6 | 1781539954 | act2Start | 22 | 10 | 30 | 10.144 | 363.822 | 373.966 |  |
| 7 | 1781972583 | act2Start | 22 | 10 | 30 | 5.459 | 174.505 | 179.963 |  |
| 8 | 1781177006 | act2Start | 21 | 10 | 30 | 4.086 | 134.538 | 138.624 |  |
| 9 | 1781186789 | act2Start | 21 | 10 | 30 | 7.401 | 247.936 | 255.337 |  |
| 10 | 1781268790 | act2Start | 21 | 10 | 30 | 10.492 | 361.325 | 371.817 |  |
| 11 | 1781605206 | act2Start | 21 | 10 | 30 | 10.981 | 389.404 | 400.385 |  |
| 12 | 1781627314 | act2Start | 20 | 10 | 30 | 9.222 | 322.007 | 331.23 |  |
| 13 | 1782128935 | act2Start | 20 | 10 | 30 | 3.252 | 110.162 | 113.414 |  |
| 14 | 1782215867 | act2Start | 20 | 10 | 30 | 9.21 | 309.098 | 318.307 |  |
| 15 | 1781278635 | act2Start | 19 | 10 | 30 | 8.872 | 300.404 | 309.276 |  |
| 16 | 1781356211 | act2Start | 19 | 10 | 30 | 4.414 | 149.754 | 154.168 |  |
| 17 | 1781515844 | act2Start | 19 | 10 | 30 | 5.149 | 185.438 | 190.587 |  |
| 18 | 1781612675 | act2Start | 19 | 10 | 30 | 8.528 | 299.391 | 307.919 |  |
| 19 | 1781862463 | act2Start | 19 | 10 | 30 | 5.407 | 212.411 | 217.818 |  |
| 20 | 1782040779 | act2Start | 19 | 10 | 30 | 11.071 | 377.707 | 388.778 |  |
| 21 | 1782117795 | act2Start | 19 | 10 | 30 | 10.63 | 390.397 | 401.027 |  |
| 22 | 1782152408 | act2Start | 19 | 10 | 30 | 5.17 | 170.815 | 175.985 |  |
| 23 | 1782170737 | act2Start | 19 | 10 | 30 | 3.262 | 109.361 | 112.623 |  |
| 24 | 1781249842 | act2Start | 18 | 10 | 30 | 5.438 | 180.558 | 185.995 |  |
| 25 | 1781522257 | act2Start | 18 | 10 | 30 | 7.33 | 258.346 | 265.676 |  |
| 26 | 1781536590 | act2Start | 18 | 10 | 30 | 13.293 | 447.485 | 460.777 |  |
| 27 | 1781589239 | act2Start | 18 | 10 | 30 | 4.114 | 136.295 | 140.409 |  |
| 28 | 1782140611 | final | 30 | 10 | 30 | 14.589 | 503.229 | 517.818 |  |
| 29 | 1782145020 | final | 27 | 10 | 30 | 10.533 | 580.693 | 591.226 |  |
| 30 | 1781356211 | final | 22 | 10 | 30 | 20.588 | 874.229 | 894.817 | totalSeconds 894.817 > slowDeckThreshold 716.994 |
| 31 | 1781268790 | final | 30 | 10 | 30 | 22.956 | 623.074 | 646.03 |  |

## Aggregates

| Resource | Horizon | Weighted value/play | Sample mean value/play | Expected delta sum | Run-scaled delta sum | Probe plays | Valid | Invalid |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| draw | shortline | 5.054 | 5.248 | 1326.055 | 530422 | 104944 | 319 | 1 |
| draw | midline | 5.184 | 5.523 | 2555.044 | 1022017.6 | 197155 | 319 | 1 |
| draw | longline | 5.061 | 5.475 | 4083.846 | 1633538.4 | 322769 | 319 | 1 |
| energyGain | shortline | 8.753 | 8.891 | 2945.961 | 1178384.4 | 134624 | 319 | 1 |
| energyGain | midline | 10.027 | 10.328 | 6604.555 | 2641822 | 263461 | 319 | 1 |
| energyGain | longline | 11.188 | 11.808 | 12425.658 | 4970263.2 | 444256 | 319 | 1 |
| starGain | shortline | 2.737 | 2.966 | 793.681 | 317472.4 | 115980 | 319 | 1 |
| starGain | midline | 5.306 | 5.771 | 2990.93 | 1196372 | 225457 | 319 | 1 |
| starGain | longline | 6.286 | 7.048 | 5889.369 | 2355747.6 | 374740 | 319 | 1 |

## Warnings

- deck=23 HeavenlyDrill+1 energyGain: energyGain shortline: probe card was not played.
- deck=23 HeavenlyDrill+1 energyGain: energyGain midline: probe card was not played.
- deck=23 HeavenlyDrill+1 energyGain: energyGain longline: probe card was not played.
- deck=23 HeavenlyDrill+1 draw: draw shortline: probe card was not played.
- deck=23 HeavenlyDrill+1 draw: draw midline: probe card was not played.
- deck=23 HeavenlyDrill+1 draw: draw longline: probe card was not played.
- deck=23 HeavenlyDrill+1 starGain: starGain shortline: probe card was not played.
- deck=23 HeavenlyDrill+1 starGain: starGain midline: probe card was not played.
- deck=23 HeavenlyDrill+1 starGain: starGain longline: probe card was not played.

## Probe Details

| Deck | Group | Layer | Copy | Card | Resource | Short plays | Short value/play | Mid plays | Mid value/play | Long plays | Long value/play |
| ---: | --- | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | floor8 | 8 | 2 | CloakOfStars | draw | 434 | 4.948 | 861 | 5.969 | 1520 | 6.297 |
| 0 | floor8 | 8 | 2 | CloakOfStars | energyGain | 403 | 3.398 | 821 | 3.318 | 1462 | 3.607 |
| 0 | floor8 | 8 | 2 | CloakOfStars | starGain | 417 | 1.254 | 868 | 1.703 | 1516 | 1.278 |
| 0 | floor8 | 8 | 4 | DefendRegent | draw | 340 | 3.322 | 709 | 3.968 | 1204 | 4.997 |
| 0 | floor8 | 8 | 4 | DefendRegent | energyGain | 429 | 5.246 | 882 | 5.454 | 1546 | 5.626 |
| 0 | floor8 | 8 | 4 | DefendRegent | starGain | 372 | 1.587 | 763 | 1.898 | 1306 | 1.885 |
| 0 | floor8 | 8 | 8 | GatherLight | draw | 421 | 2.663 | 842 | 4.174 | 1420 | 4.999 |
| 0 | floor8 | 8 | 8 | GatherLight | energyGain | 422 | 5.903 | 890 | 6.213 | 1529 | 6.585 |
| 0 | floor8 | 8 | 8 | GatherLight | starGain | 413 | 1.14 | 862 | 1.846 | 1466 | 1.783 |
| 0 | floor8 | 8 | 11 | NeowsFury | draw | 363 | 2.549 | 390 | 2.098 | 400 | 4.161 |
| 0 | floor8 | 8 | 11 | NeowsFury | energyGain | 400 | 5.701 | 400 | 5.27 | 400 | 4.464 |
| 0 | floor8 | 8 | 11 | NeowsFury | starGain | 364 | 1.132 | 393 | 1.923 | 400 | 2.802 |
| 0 | floor8 | 8 | 12 | PillarOfCreation | draw | 400 | 3.539 | 400 | 1.868 | 400 | 3.147 |
| 0 | floor8 | 8 | 12 | PillarOfCreation | energyGain | 400 | 5.831 | 400 | 5.661 | 400 | 5.177 |
| 0 | floor8 | 8 | 12 | PillarOfCreation | starGain | 400 | 1.293 | 400 | 2.302 | 400 | 2.85 |
| 0 | floor8 | 8 | 14 | StrikeRegent | draw | 315 | 3.19 | 627 | 4.552 | 1048 | 5.386 |
| 0 | floor8 | 8 | 14 | StrikeRegent | energyGain | 419 | 4.77 | 875 | 5.089 | 1528 | 5.328 |
| 0 | floor8 | 8 | 14 | StrikeRegent | starGain | 373 | 1.117 | 772 | 0.939 | 1300 | 1.074 |
| 0 | floor8 | 8 | 15 | StrikeRegent | draw | 298 | 3.772 | 579 | 4.502 | 970 | 5.816 |
| 0 | floor8 | 8 | 15 | StrikeRegent | energyGain | 419 | 4.578 | 865 | 4.908 | 1535 | 5.313 |
| 0 | floor8 | 8 | 15 | StrikeRegent | starGain | 381 | 1.115 | 770 | 0.991 | 1273 | 1.651 |
| 0 | floor8 | 8 | 16 | StrikeRegent | draw | 277 | 3.386 | 566 | 3.731 | 926 | 6.27 |
| 0 | floor8 | 8 | 16 | StrikeRegent | energyGain | 421 | 4.693 | 914 | 4.308 | 1537 | 5.116 |
| 0 | floor8 | 8 | 16 | StrikeRegent | starGain | 366 | 1.183 | 774 | 1.414 | 1293 | 1.254 |
| 0 | floor8 | 8 | 17 | StrikeRegent | draw | 262 | 3.588 | 513 | 3.716 | 878 | 5.823 |
| 0 | floor8 | 8 | 17 | StrikeRegent | energyGain | 420 | 4.751 | 897 | 5.084 | 1543 | 5.212 |
| 0 | floor8 | 8 | 17 | StrikeRegent | starGain | 365 | 0.739 | 766 | 0.996 | 1296 | 0.859 |
| 0 | floor8 | 8 | 18 | Venerate | draw | 387 | 3.235 | 767 | 4.7 | 1331 | 5.192 |
| 0 | floor8 | 8 | 18 | Venerate | energyGain | 421 | 4.794 | 877 | 4.484 | 1529 | 5.237 |
| 0 | floor8 | 8 | 18 | Venerate | starGain | 377 | 0.375 | 753 | 1.295 | 1299 | 1.17 |
| 1 | floor8 | 8 | 1 | ChildOfTheStars | draw | 400 | 6.466 | 400 | 6.009 | 400 | 2.643 |
| 1 | floor8 | 8 | 1 | ChildOfTheStars | energyGain | 400 | 6.516 | 400 | 6.359 | 400 | 5.745 |
| 1 | floor8 | 8 | 1 | ChildOfTheStars | starGain | 400 | 1.732 | 400 | 1.844 | 400 | 2.403 |
| 1 | floor8 | 8 | 2 | CloakOfStars | draw | 587 | 5.94 | 1189 | 4.732 | 1984 | 3.535 |
| 1 | floor8 | 8 | 2 | CloakOfStars | energyGain | 558 | 4.049 | 1179 | 4.582 | 2014 | 4.592 |
| 1 | floor8 | 8 | 2 | CloakOfStars | starGain | 569 | 0.473 | 1161 | 0.905 | 1974 | 0.66 |
| 1 | floor8 | 8 | 3 | CollisionCourse | draw | 600 | 6.904 | 1198 | 4.006 | 2028 | 3.923 |
| 1 | floor8 | 8 | 3 | CollisionCourse | energyGain | 564 | 3.6 | 1212 | 5.05 | 2068 | 5.517 |
| 1 | floor8 | 8 | 3 | CollisionCourse | starGain | 551 | 0.38 | 1152 | 0.562 | 1968 | 0.107 |
| 1 | floor8 | 8 | 6 | DefendRegent | draw | 448 | 6.664 | 920 | 4.678 | 1613 | 4.139 |
| 1 | floor8 | 8 | 6 | DefendRegent | energyGain | 551 | 4.986 | 1148 | 5.744 | 2002 | 5.835 |
| 1 | floor8 | 8 | 6 | DefendRegent | starGain | 515 | 0.512 | 1086 | 1.06 | 1872 | 0.342 |
| 1 | floor8 | 8 | 7 | DefendRegent | draw | 415 | 6.376 | 865 | 5.116 | 1542 | 4.434 |
| 1 | floor8 | 8 | 7 | DefendRegent | energyGain | 558 | 4.898 | 1135 | 5.753 | 1981 | 6.357 |
| 1 | floor8 | 8 | 7 | DefendRegent | starGain | 537 | 0.399 | 1084 | 0.797 | 1872 | 0.082 |
| 1 | floor8 | 8 | 8 | FallingStar | draw | 618 | 6.15 | 1218 | 4.296 | 2056 | 3.726 |
| 1 | floor8 | 8 | 8 | FallingStar | energyGain | 581 | 4.53 | 1232 | 4.619 | 2063 | 4.908 |
| 1 | floor8 | 8 | 8 | FallingStar | starGain | 583 | 1.164 | 1225 | 0.755 | 2028 | 0.744 |
| 1 | floor8 | 8 | 9 | Glow | draw | 417 | 6.384 | 842 | 3.765 | 1458 | 3.664 |
| 1 | floor8 | 8 | 9 | Glow | energyGain | 566 | 5.51 | 1180 | 5.573 | 1987 | 5.863 |
| 1 | floor8 | 8 | 9 | Glow | starGain | 555 | -0.179 | 1155 | 0.392 | 1967 | -0.583 |
| 1 | floor8 | 8 | 12 | HiddenCache | draw | 559 | 6.117 | 1158 | 4.82 | 1970 | 4.46 |
| 1 | floor8 | 8 | 12 | HiddenCache | energyGain | 555 | 5.318 | 1168 | 5.168 | 2014 | 5.48 |
| 1 | floor8 | 8 | 12 | HiddenCache | starGain | 530 | 0.346 | 1127 | 0.066 | 1938 | 0.303 |
| 1 | floor8 | 8 | 16 | StrikeRegent | draw | 376 | 5.735 | 795 | 4.949 | 1417 | 4.441 |
| 1 | floor8 | 8 | 16 | StrikeRegent | energyGain | 540 | 4.333 | 1149 | 5.324 | 1977 | 5.41 |
| 1 | floor8 | 8 | 16 | StrikeRegent | starGain | 515 | -0.026 | 1061 | 0.556 | 1855 | -0.084 |
| 1 | floor8 | 8 | 17 | Venerate | draw | 530 | 6.047 | 1093 | 4.048 | 1875 | 3.881 |
| 1 | floor8 | 8 | 17 | Venerate | energyGain | 564 | 4.399 | 1149 | 4.863 | 1995 | 5.378 |
| 1 | floor8 | 8 | 17 | Venerate | starGain | 535 | -0.452 | 1086 | -0.25 | 1870 | -0.265 |
| 2 | floor8 | 8 | 4 | CollisionCourse | draw | 513 | 5.985 | 969 | 6.064 | 1553 | 5.49 |
| 2 | floor8 | 8 | 4 | CollisionCourse | energyGain | 496 | 6.87 | 950 | 6.709 | 1591 | 8.151 |
| 2 | floor8 | 8 | 4 | CollisionCourse | starGain | 495 | 6.656 | 959 | 10.529 | 1575 | 11.308 |
| 2 | floor8 | 8 | 5 | DefendRegent | draw | 437 | 4.4 | 821 | 3.831 | 1304 | 3.507 |
| 2 | floor8 | 8 | 5 | DefendRegent | energyGain | 506 | 7.416 | 969 | 7.141 | 1572 | 7.261 |
| 2 | floor8 | 8 | 5 | DefendRegent | starGain | 452 | 6.105 | 870 | 9.589 | 1412 | 10.93 |
| 2 | floor8 | 8 | 8 | DefendRegent | draw | 373 | 5.33 | 697 | 3.777 | 1128 | 3.144 |
| 2 | floor8 | 8 | 8 | DefendRegent | energyGain | 519 | 7.175 | 982 | 7.259 | 1618 | 7.649 |
| 2 | floor8 | 8 | 8 | DefendRegent | starGain | 440 | 6.235 | 873 | 9.009 | 1429 | 10.313 |
| 2 | floor8 | 8 | 9 | FallingStar | draw | 418 | 6.701 | 649 | 6.723 | 968 | 6.506 |
| 2 | floor8 | 8 | 9 | FallingStar | energyGain | 382 | 8.197 | 615 | 7.725 | 954 | 9.001 |
| 2 | floor8 | 8 | 9 | FallingStar | starGain | 441 | 7.044 | 683 | 10.382 | 1043 | 10.84 |
| 2 | floor8 | 8 | 10 | Glow | draw | 387 | 6.049 | 706 | 6.589 | 1108 | 6.094 |
| 2 | floor8 | 8 | 10 | Glow | energyGain | 500 | 10.694 | 942 | 13.298 | 1554 | 14.435 |
| 2 | floor8 | 8 | 10 | Glow | starGain | 460 | 8.596 | 867 | 14.965 | 1441 | 14.871 |
| 2 | floor8 | 8 | 11 | ManifestAuthority+1 | draw | 500 | 3.854 | 953 | 2.229 | 1552 | 2.098 |
| 2 | floor8 | 8 | 11 | ManifestAuthority+1 | energyGain | 495 | 10.357 | 961 | 10.518 | 1606 | 11.673 |
| 2 | floor8 | 8 | 11 | ManifestAuthority+1 | starGain | 479 | 6.904 | 954 | 10.397 | 1569 | 11.276 |
| 2 | floor8 | 8 | 12 | Reflect | draw | 391 | 4.735 | 513 | 5.113 | 633 | 4.011 |
| 2 | floor8 | 8 | 12 | Reflect | energyGain | 399 | 8.799 | 530 | 9.687 | 648 | 10.304 |
| 2 | floor8 | 8 | 12 | Reflect | starGain | 413 | 7.049 | 568 | 10.518 | 715 | 10.379 |
| 2 | floor8 | 8 | 13 | StrikeRegent | draw | 366 | 4.296 | 617 | 4.879 | 990 | 3.817 |
| 2 | floor8 | 8 | 13 | StrikeRegent | energyGain | 508 | 6.857 | 959 | 6.453 | 1566 | 6.911 |
| 2 | floor8 | 8 | 13 | StrikeRegent | starGain | 462 | 5.414 | 866 | 9.769 | 1417 | 9.665 |
| 2 | floor8 | 8 | 15 | StrikeRegent | draw | 328 | 4.496 | 559 | 4.2 | 898 | 2.849 |
| 2 | floor8 | 8 | 15 | StrikeRegent | energyGain | 504 | 6.475 | 1000 | 7.073 | 1606 | 7.424 |
| 2 | floor8 | 8 | 15 | StrikeRegent | starGain | 456 | 5.686 | 890 | 8.854 | 1453 | 9.487 |
| 2 | floor8 | 8 | 16 | StrikeRegent | draw | 289 | 5.46 | 508 | 4.35 | 818 | 2.469 |
| 2 | floor8 | 8 | 16 | StrikeRegent | energyGain | 501 | 5.344 | 979 | 5.616 | 1595 | 7.049 |
| 2 | floor8 | 8 | 16 | StrikeRegent | starGain | 459 | 5.454 | 878 | 8.978 | 1445 | 9.643 |
| 3 | floor8 | 8 | 2 | CloakOfStars | draw | 483 | 3.964 | 708 | 5.34 | 1045 | 5.058 |
| 3 | floor8 | 8 | 2 | CloakOfStars | energyGain | 447 | 4.695 | 602 | 4.506 | 818 | 4.934 |
| 3 | floor8 | 8 | 2 | CloakOfStars | starGain | 498 | 2.192 | 1012 | 7.391 | 1718 | 9.454 |
| 3 | floor8 | 8 | 3 | CosmicIndifference | draw | 1035 | 2.06 | 2560 | 2.536 | 4910 | 2.441 |
| 3 | floor8 | 8 | 3 | CosmicIndifference | energyGain | 1121 | 5.018 | 2721 | 4.897 | 5121 | 5.027 |
| 3 | floor8 | 8 | 3 | CosmicIndifference | starGain | 1039 | 3.304 | 2571 | 6.7 | 4933 | 7.679 |
| 3 | floor8 | 8 | 4 | DefendRegent | draw | 401 | 2.158 | 957 | 1.804 | 1745 | 1.515 |
| 3 | floor8 | 8 | 4 | DefendRegent | energyGain | 417 | 5.639 | 801 | 5.893 | 1378 | 6.372 |
| 3 | floor8 | 8 | 4 | DefendRegent | starGain | 416 | 1.825 | 857 | 6.905 | 1487 | 8.672 |
| 3 | floor8 | 8 | 5 | DefendRegent | draw | 390 | 2.129 | 855 | 2.307 | 1546 | 2.224 |
| 3 | floor8 | 8 | 5 | DefendRegent | energyGain | 407 | 5.519 | 791 | 5.843 | 1332 | 6.353 |
| 3 | floor8 | 8 | 5 | DefendRegent | starGain | 414 | 1.902 | 831 | 7.066 | 1437 | 9.011 |
| 3 | floor8 | 8 | 6 | DefendRegent | draw | 362 | 2.2 | 794 | 2.573 | 1406 | 2.284 |
| 3 | floor8 | 8 | 6 | DefendRegent | energyGain | 399 | 5.725 | 777 | 6.242 | 1338 | 6.621 |
| 3 | floor8 | 8 | 6 | DefendRegent | starGain | 413 | 1.731 | 816 | 7.019 | 1438 | 9.159 |
| 3 | floor8 | 8 | 7 | DefendRegent | draw | 340 | 1.979 | 743 | 2.375 | 1321 | 1.901 |
| 3 | floor8 | 8 | 7 | DefendRegent | energyGain | 393 | 5.906 | 789 | 6.155 | 1346 | 5.817 |
| 3 | floor8 | 8 | 7 | DefendRegent | starGain | 411 | 1.598 | 835 | 7.072 | 1449 | 9.024 |
| 3 | floor8 | 8 | 9 | Havoc+1 | draw | 431 | 4.146 | 870 | 5.458 | 1471 | 5.889 |
| 3 | floor8 | 8 | 9 | Havoc+1 | energyGain | 397 | 4.496 | 793 | 3.385 | 1383 | 3.741 |
| 3 | floor8 | 8 | 9 | Havoc+1 | starGain | 429 | 2.49 | 827 | 7.272 | 1462 | 8.801 |
| 3 | floor8 | 8 | 11 | Quasar | draw | 294 | 3.509 | 383 | 5.022 | 498 | 5.569 |
| 3 | floor8 | 8 | 11 | Quasar | energyGain | 325 | 6.886 | 431 | 8.25 | 557 | 10.536 |
| 3 | floor8 | 8 | 11 | Quasar | starGain | 318 | 2.652 | 426 | 9.831 | 554 | 12.853 |
| 3 | floor8 | 8 | 14 | StrikeRegent | draw | 279 | 2.297 | 586 | 2.396 | 992 | 2.15 |
| 3 | floor8 | 8 | 14 | StrikeRegent | energyGain | 403 | 5.243 | 796 | 5.005 | 1357 | 5.311 |
| 3 | floor8 | 8 | 14 | StrikeRegent | starGain | 414 | 1.418 | 805 | 6.647 | 1410 | 8.163 |
| 3 | floor8 | 8 | 16 | Venerate | draw | 376 | 3.195 | 738 | 3.16 | 1301 | 3.116 |
| 3 | floor8 | 8 | 16 | Venerate | energyGain | 421 | 5.603 | 827 | 7.255 | 1450 | 8.254 |
| 3 | floor8 | 8 | 16 | Venerate | starGain | 407 | 1.444 | 806 | 8.02 | 1414 | 9.548 |
| 4 | floor8 | 8 | 1 | Charge | draw | 131 | 8.037 | 226 | 8.009 | 398 | 14.126 |
| 4 | floor8 | 8 | 1 | Charge | energyGain | 409 | 16.856 | 863 | 16.804 | 1624 | 21.369 |
| 4 | floor8 | 8 | 1 | Charge | starGain | 62 | 6.645 | 132 | 12.588 | 236 | 13.78 |
| 4 | floor8 | 8 | 2 | CloakOfStars | draw | 465 | 4.788 | 763 | 4.983 | 1157 | 5.213 |
| 4 | floor8 | 8 | 2 | CloakOfStars | energyGain | 441 | 7.189 | 705 | 8.466 | 1058 | 9.943 |
| 4 | floor8 | 8 | 2 | CloakOfStars | starGain | 480 | 1.049 | 900 | 3.704 | 1528 | 6.358 |
| 4 | floor8 | 8 | 3 | CosmicIndifference | draw | 923 | 3.831 | 1829 | 3.191 | 2846 | 3.749 |
| 4 | floor8 | 8 | 3 | CosmicIndifference | energyGain | 1119 | 7.837 | 2632 | 9.586 | 4771 | 10.177 |
| 4 | floor8 | 8 | 3 | CosmicIndifference | starGain | 969 | 1.962 | 2081 | 3.214 | 3344 | 4.05 |
| 4 | floor8 | 8 | 4 | DefendRegent | draw | 375 | 3.481 | 682 | 2.468 | 1110 | 3.194 |
| 4 | floor8 | 8 | 4 | DefendRegent | energyGain | 421 | 6.982 | 832 | 7.469 | 1444 | 8.268 |
| 4 | floor8 | 8 | 4 | DefendRegent | starGain | 418 | -1.037 | 778 | 2.75 | 1313 | 4.812 |
| 4 | floor8 | 8 | 5 | DefendRegent | draw | 357 | 3.69 | 652 | 2.694 | 1067 | 2.899 |
| 4 | floor8 | 8 | 5 | DefendRegent | energyGain | 413 | 7.738 | 818 | 7.905 | 1430 | 9.04 |
| 4 | floor8 | 8 | 5 | DefendRegent | starGain | 412 | -0.452 | 787 | 2.773 | 1304 | 5.276 |
| 4 | floor8 | 8 | 6 | DefendRegent | draw | 336 | 4.26 | 608 | 1.786 | 985 | 3.533 |
| 4 | floor8 | 8 | 6 | DefendRegent | energyGain | 415 | 6.747 | 830 | 7.496 | 1443 | 8.07 |
| 4 | floor8 | 8 | 6 | DefendRegent | starGain | 416 | -0.258 | 793 | 2.561 | 1314 | 4.809 |
| 4 | floor8 | 8 | 7 | DefendRegent | draw | 294 | 4.971 | 559 | 3.154 | 925 | 4.456 |
| 4 | floor8 | 8 | 7 | DefendRegent | energyGain | 402 | 7.529 | 808 | 7.401 | 1417 | 7.58 |
| 4 | floor8 | 8 | 7 | DefendRegent | starGain | 391 | -0.445 | 767 | 2.328 | 1290 | 5.165 |
| 4 | floor8 | 8 | 12 | StrikeRegent | draw | 268 | 3.873 | 500 | 3.118 | 806 | 5.022 |
| 4 | floor8 | 8 | 12 | StrikeRegent | energyGain | 409 | 6.5 | 799 | 6.615 | 1398 | 7.169 |
| 4 | floor8 | 8 | 12 | StrikeRegent | starGain | 391 | -0.771 | 746 | 1.714 | 1229 | 4.338 |
| 4 | floor8 | 8 | 13 | StrikeRegent | draw | 265 | 3.58 | 492 | 2.13 | 796 | 3.558 |
| 4 | floor8 | 8 | 13 | StrikeRegent | energyGain | 419 | 6.741 | 817 | 7.258 | 1436 | 7.653 |
| 4 | floor8 | 8 | 13 | StrikeRegent | starGain | 398 | -0.992 | 748 | 2.211 | 1266 | 4.215 |
| 4 | floor8 | 8 | 14 | StrikeRegent | draw | 254 | 3.934 | 467 | 2.109 | 752 | 4.199 |
| 4 | floor8 | 8 | 14 | StrikeRegent | energyGain | 410 | 6.353 | 815 | 6.91 | 1453 | 7.315 |
| 4 | floor8 | 8 | 14 | StrikeRegent | starGain | 388 | -1.379 | 755 | 2.406 | 1277 | 4.622 |
| 5 | floor8 | 8 | 1 | Bulwark+1 | draw | 179 | 0.451 | 310 | 1.403 | 444 | -0.395 |
| 5 | floor8 | 8 | 1 | Bulwark+1 | energyGain | 517 | 11.046 | 1089 | 14.309 | 1827 | 23.321 |
| 5 | floor8 | 8 | 1 | Bulwark+1 | starGain | 333 | 3.765 | 657 | 10.248 | 1064 | 15.571 |
| 5 | floor8 | 8 | 2 | ChildOfTheStars | draw | 396 | 1.565 | 400 | 0.575 | 400 | 0.015 |
| 5 | floor8 | 8 | 2 | ChildOfTheStars | energyGain | 400 | 12.729 | 400 | 15.257 | 400 | 16.621 |
| 5 | floor8 | 8 | 2 | ChildOfTheStars | starGain | 400 | 5.514 | 400 | 15.678 | 400 | 16.588 |
| 5 | floor8 | 8 | 4 | DefendRegent | draw | 432 | 0.781 | 904 | 0.732 | 1481 | 1.249 |
| 5 | floor8 | 8 | 4 | DefendRegent | energyGain | 660 | 9.029 | 1361 | 10.441 | 2309 | 10.801 |
| 5 | floor8 | 8 | 4 | DefendRegent | starGain | 491 | 3.795 | 1056 | 7.595 | 1781 | 8.189 |
| 5 | floor8 | 8 | 5 | DefendRegent | draw | 372 | 1.33 | 797 | 1.151 | 1318 | 0.951 |
| 5 | floor8 | 8 | 5 | DefendRegent | energyGain | 665 | 9.416 | 1360 | 10.568 | 2311 | 10.449 |
| 5 | floor8 | 8 | 5 | DefendRegent | starGain | 480 | 3.765 | 1045 | 7.45 | 1789 | 7.778 |
| 5 | floor8 | 8 | 10 | ManifestAuthority | draw | 416 | 0.156 | 795 | -0.389 | 1257 | 0.109 |
| 5 | floor8 | 8 | 10 | ManifestAuthority | energyGain | 652 | 13.699 | 1345 | 15.615 | 2129 | 18.19 |
| 5 | floor8 | 8 | 10 | ManifestAuthority | starGain | 525 | 3.833 | 1128 | 8.121 | 1849 | 9.508 |
| 5 | floor8 | 8 | 11 | PhotonCut+1 | draw | 827 | 1.41 | 1870 | 1.247 | 3362 | 1.135 |
| 5 | floor8 | 8 | 11 | PhotonCut+1 | energyGain | 1081 | 12.928 | 2744 | 15.409 | 5324 | 15.631 |
| 5 | floor8 | 8 | 11 | PhotonCut+1 | starGain | 981 | 2.651 | 2265 | 5.875 | 4125 | 6.553 |
| 5 | floor8 | 8 | 12 | StrikeRegent | draw | 152 | 0.808 | 243 | 0 | 356 | -0.837 |
| 5 | floor8 | 8 | 12 | StrikeRegent | energyGain | 594 | 6.51 | 1225 | 6.81 | 2109 | 6.732 |
| 5 | floor8 | 8 | 12 | StrikeRegent | starGain | 319 | 2.868 | 567 | 6.274 | 929 | 4.402 |
| 5 | floor8 | 8 | 13 | StrikeRegent | draw | 151 | 1.054 | 226 | 0.832 | 298 | -0.909 |
| 5 | floor8 | 8 | 13 | StrikeRegent | energyGain | 581 | 6.083 | 1242 | 6.763 | 2124 | 6.813 |
| 5 | floor8 | 8 | 13 | StrikeRegent | starGain | 312 | 2.838 | 596 | 5.858 | 935 | 4.456 |
| 5 | floor8 | 8 | 15 | StrikeRegent | draw | 128 | 1.781 | 170 | 0.901 | 236 | -2.376 |
| 5 | floor8 | 8 | 15 | StrikeRegent | energyGain | 596 | 6.527 | 1235 | 6.893 | 2104 | 6.869 |
| 5 | floor8 | 8 | 15 | StrikeRegent | starGain | 299 | 2.673 | 564 | 6.482 | 871 | 4.89 |
| 5 | floor8 | 8 | 16 | Venerate | draw | 292 | 1.704 | 673 | 2.442 | 1201 | 2.354 |
| 5 | floor8 | 8 | 16 | Venerate | energyGain | 603 | 5.642 | 1287 | 10.702 | 2191 | 12.818 |
| 5 | floor8 | 8 | 16 | Venerate | starGain | 450 | -4.316 | 920 | 3.221 | 1576 | 5.381 |
| 6 | act2Start | 17 | 1 | Charge | draw | 206 | 9.07 | 444 | 10.805 | 794 | 8.945 |
| 6 | act2Start | 17 | 1 | Charge | energyGain | 432 | 16.726 | 968 | 13.401 | 1843 | 9.705 |
| 6 | act2Start | 17 | 1 | Charge | starGain | 167 | 11.461 | 390 | 12.244 | 774 | 11.717 |
| 6 | act2Start | 17 | 2 | ChildOfTheStars+1 | draw | 383 | 2.112 | 390 | 3.528 | 391 | 3.011 |
| 6 | act2Start | 17 | 2 | ChildOfTheStars+1 | energyGain | 390 | 9.375 | 391 | 8.254 | 391 | 12.005 |
| 6 | act2Start | 17 | 2 | ChildOfTheStars+1 | starGain | 390 | 7.355 | 391 | 10.382 | 391 | 10.405 |
| 6 | act2Start | 17 | 3 | CloakOfStars | draw | 457 | 3.951 | 949 | 5.141 | 1617 | 5.163 |
| 6 | act2Start | 17 | 3 | CloakOfStars | energyGain | 463 | 6.898 | 921 | 7.25 | 1606 | 7.246 |
| 6 | act2Start | 17 | 3 | CloakOfStars | starGain | 481 | 4.998 | 991 | 5.905 | 1712 | 5.888 |
| 6 | act2Start | 17 | 5 | Conqueror | draw | 262 | 2.791 | 557 | 1.018 | 963 | 3.218 |
| 6 | act2Start | 17 | 5 | Conqueror | energyGain | 432 | 9.364 | 903 | 7.535 | 1562 | 8.347 |
| 6 | act2Start | 17 | 5 | Conqueror | starGain | 317 | 4.206 | 592 | 4.795 | 941 | 5.102 |
| 6 | act2Start | 17 | 6 | DecisionsDecisions | draw | 105 | 3.92 | 228 | 5.1 | 306 | 4.469 |
| 6 | act2Start | 17 | 6 | DecisionsDecisions | energyGain | 109 | 9.299 | 234 | 9.217 | 306 | 10.471 |
| 6 | act2Start | 17 | 6 | DecisionsDecisions | starGain | 113 | 7.745 | 236 | 8.566 | 307 | 10.427 |
| 6 | act2Start | 17 | 7 | DefendRegent | draw | 376 | 2.636 | 826 | 4.007 | 1461 | 3.916 |
| 6 | act2Start | 17 | 7 | DefendRegent | energyGain | 452 | 6.598 | 948 | 7.359 | 1699 | 6.962 |
| 6 | act2Start | 17 | 7 | DefendRegent | starGain | 386 | 5.431 | 866 | 5.79 | 1538 | 5.578 |
| 6 | act2Start | 17 | 11 | FallingStar | draw | 422 | 3.718 | 860 | 4.973 | 1493 | 4.629 |
| 6 | act2Start | 17 | 11 | FallingStar | energyGain | 418 | 7.401 | 871 | 8.505 | 1499 | 8.606 |
| 6 | act2Start | 17 | 11 | FallingStar | starGain | 433 | 5.442 | 909 | 6.189 | 1584 | 5.971 |
| 6 | act2Start | 17 | 19 | StrikeRegent | draw | 297 | 0.66 | 653 | 2.731 | 1139 | 3.514 |
| 6 | act2Start | 17 | 19 | StrikeRegent | energyGain | 466 | 6.323 | 964 | 6.33 | 1699 | 5.895 |
| 6 | act2Start | 17 | 19 | StrikeRegent | starGain | 390 | 4.652 | 853 | 4.924 | 1521 | 5.389 |
| 6 | act2Start | 17 | 20 | StrikeRegent | draw | 279 | 2.506 | 597 | 4.264 | 1088 | 3.89 |
| 6 | act2Start | 17 | 20 | StrikeRegent | energyGain | 449 | 6.862 | 966 | 6.18 | 1686 | 6.432 |
| 6 | act2Start | 17 | 20 | StrikeRegent | starGain | 378 | 5.65 | 843 | 6.171 | 1472 | 6.078 |
| 6 | act2Start | 17 | 21 | Venerate | draw | 427 | 3.415 | 898 | 3.487 | 1493 | 3.416 |
| 6 | act2Start | 17 | 21 | Venerate | energyGain | 463 | 7.279 | 952 | 8.415 | 1613 | 6.908 |
| 6 | act2Start | 17 | 21 | Venerate | starGain | 404 | 4.176 | 858 | 5.674 | 1460 | 5.236 |
| 7 | act2Start | 17 | 1 | AstralPulse+1 | draw | 320 | 9.525 | 611 | 8.391 | 932 | 4.276 |
| 7 | act2Start | 17 | 1 | AstralPulse+1 | energyGain | 319 | 7.74 | 616 | 6.699 | 981 | 6.463 |
| 7 | act2Start | 17 | 1 | AstralPulse+1 | starGain | 321 | 2.156 | 620 | 0.681 | 975 | 0.406 |
| 7 | act2Start | 17 | 9 | FallingStar | draw | 321 | 10.247 | 608 | 6.432 | 965 | 6.943 |
| 7 | act2Start | 17 | 9 | FallingStar | energyGain | 309 | 10.409 | 604 | 8.431 | 969 | 7.709 |
| 7 | act2Start | 17 | 9 | FallingStar | starGain | 322 | 2.894 | 613 | 2.059 | 991 | 1.314 |
| 7 | act2Start | 17 | 10 | GatherLight | draw | 326 | 7.692 | 612 | 2.792 | 926 | 2.451 |
| 7 | act2Start | 17 | 10 | GatherLight | energyGain | 364 | 8.835 | 668 | 6.802 | 1037 | 6.829 |
| 7 | act2Start | 17 | 10 | GatherLight | starGain | 331 | 1.584 | 610 | 1.618 | 950 | 0.897 |
| 7 | act2Start | 17 | 11 | HiddenCache+1 | draw | 344 | 6.799 | 632 | 4.677 | 984 | 3.712 |
| 7 | act2Start | 17 | 11 | HiddenCache+1 | energyGain | 372 | 8.334 | 674 | 7.331 | 1052 | 6.435 |
| 7 | act2Start | 17 | 11 | HiddenCache+1 | starGain | 368 | -0.297 | 678 | -1.015 | 1044 | -0.238 |
| 7 | act2Start | 17 | 12 | ManifestAuthority+1 | draw | 349 | 7.07 | 642 | 5.027 | 998 | 3.103 |
| 7 | act2Start | 17 | 12 | ManifestAuthority+1 | energyGain | 357 | 13.621 | 660 | 11.932 | 1033 | 12.586 |
| 7 | act2Start | 17 | 12 | ManifestAuthority+1 | starGain | 355 | 2.584 | 670 | 1.311 | 1051 | 1.672 |
| 7 | act2Start | 17 | 13 | NeowsFury | draw | 269 | 10.632 | 357 | 1.729 | 386 | 0.856 |
| 7 | act2Start | 17 | 13 | NeowsFury | energyGain | 364 | 8.851 | 400 | 8.366 | 400 | 9.401 |
| 7 | act2Start | 17 | 13 | NeowsFury | starGain | 290 | 2.157 | 380 | 2.565 | 396 | 1.085 |
| 7 | act2Start | 17 | 14 | PillarOfCreation | draw | 357 | 8.594 | 400 | 0.142 | 400 | 1.916 |
| 7 | act2Start | 17 | 14 | PillarOfCreation | energyGain | 357 | 10.783 | 400 | 7.826 | 400 | 12.309 |
| 7 | act2Start | 17 | 14 | PillarOfCreation | starGain | 357 | 2.292 | 400 | 2.668 | 400 | 2.506 |
| 7 | act2Start | 17 | 15 | PillarOfCreation+1 | draw | 370 | 9.418 | 400 | 2.561 | 400 | -1.316 |
| 7 | act2Start | 17 | 15 | PillarOfCreation+1 | energyGain | 370 | 10.983 | 400 | 7.98 | 400 | 10.231 |
| 7 | act2Start | 17 | 15 | PillarOfCreation+1 | starGain | 370 | 2.678 | 400 | 3.144 | 400 | 1.321 |
| 7 | act2Start | 17 | 18 | StrikeRegent | draw | 179 | 12.536 | 340 | 4.272 | 597 | 3.806 |
| 7 | act2Start | 17 | 18 | StrikeRegent | energyGain | 363 | 6.673 | 665 | 4.908 | 1047 | 4.907 |
| 7 | act2Start | 17 | 18 | StrikeRegent | starGain | 253 | 0.999 | 477 | 1.291 | 814 | -1.153 |
| 7 | act2Start | 17 | 19 | StrikeRegent | draw | 191 | 9.805 | 349 | 4.474 | 580 | 2.484 |
| 7 | act2Start | 17 | 19 | StrikeRegent | energyGain | 361 | 6.743 | 663 | 4.38 | 1051 | 5.709 |
| 7 | act2Start | 17 | 19 | StrikeRegent | starGain | 259 | 1.547 | 491 | 0.725 | 812 | 0.279 |
| 8 | act2Start | 17 | 1 | Bulwark+1 | draw | 378 | 2.238 | 641 | 0.635 | 979 | 2.202 |
| 8 | act2Start | 17 | 1 | Bulwark+1 | energyGain | 457 | 10.08 | 939 | 14.139 | 1549 | 19.437 |
| 8 | act2Start | 17 | 1 | Bulwark+1 | starGain | 402 | 0.578 | 725 | 3.53 | 1085 | 5.074 |
| 8 | act2Start | 17 | 2 | Charge | draw | 61 | 9.2 | 115 | 12.623 | 209 | 9.937 |
| 8 | act2Start | 17 | 2 | Charge | energyGain | 368 | 17.695 | 773 | 21.619 | 1386 | 27.176 |
| 8 | act2Start | 17 | 2 | Charge | starGain | 42 | 0.419 | 87 | 2.041 | 143 | 6.162 |
| 8 | act2Start | 17 | 6 | DefendRegent | draw | 244 | 2.718 | 446 | 0.092 | 761 | 0.813 |
| 8 | act2Start | 17 | 6 | DefendRegent | energyGain | 385 | 8.284 | 765 | 8.177 | 1285 | 9.127 |
| 8 | act2Start | 17 | 6 | DefendRegent | starGain | 318 | -0.566 | 590 | -0.755 | 964 | 1.611 |
| 8 | act2Start | 17 | 8 | DefendRegent | draw | 218 | 3.701 | 403 | 3.252 | 697 | 2.813 |
| 8 | act2Start | 17 | 8 | DefendRegent | energyGain | 377 | 8.837 | 750 | 8.923 | 1284 | 9.845 |
| 8 | act2Start | 17 | 8 | DefendRegent | starGain | 310 | -0.133 | 595 | 0.88 | 965 | 1.386 |
| 8 | act2Start | 17 | 12 | IAmInvincible | draw | 395 | 4.087 | 744 | 2.584 | 1243 | 2.431 |
| 8 | act2Start | 17 | 12 | IAmInvincible | energyGain | 409 | 10.19 | 779 | 12.688 | 1364 | 14.34 |
| 8 | act2Start | 17 | 12 | IAmInvincible | starGain | 380 | 0.518 | 719 | 1.442 | 1206 | 2.91 |
| 8 | act2Start | 17 | 13 | ManifestAuthority+1 | draw | 388 | 1.711 | 730 | 1.707 | 1214 | 2.135 |
| 8 | act2Start | 17 | 13 | ManifestAuthority+1 | energyGain | 401 | 12.001 | 749 | 13.082 | 1297 | 17.913 |
| 8 | act2Start | 17 | 13 | ManifestAuthority+1 | starGain | 377 | 0.502 | 726 | 2.422 | 1234 | 3.406 |
| 8 | act2Start | 17 | 17 | StrikeRegent | draw | 173 | 2.812 | 314 | 2.65 | 542 | 0.262 |
| 8 | act2Start | 17 | 17 | StrikeRegent | energyGain | 365 | 7.639 | 739 | 7.356 | 1263 | 8.229 |
| 8 | act2Start | 17 | 17 | StrikeRegent | starGain | 306 | -1.02 | 565 | -0.792 | 953 | -0.885 |
| 8 | act2Start | 17 | 18 | StrikeRegent | draw | 160 | 2.98 | 303 | 3.213 | 508 | 0.984 |
| 8 | act2Start | 17 | 18 | StrikeRegent | energyGain | 369 | 6.647 | 747 | 7.118 | 1262 | 8.285 |
| 8 | act2Start | 17 | 18 | StrikeRegent | starGain | 293 | -1.323 | 554 | -1.672 | 922 | -0.586 |
| 8 | act2Start | 17 | 19 | StrikeRegent | draw | 129 | 1.088 | 235 | 2.62 | 420 | 2.663 |
| 8 | act2Start | 17 | 19 | StrikeRegent | energyGain | 373 | 6.754 | 738 | 7.522 | 1258 | 7.723 |
| 8 | act2Start | 17 | 19 | StrikeRegent | starGain | 298 | -2.36 | 569 | -1.873 | 940 | -1.119 |
| 8 | act2Start | 17 | 20 | Venerate | draw | 251 | 2.269 | 457 | 1.893 | 795 | 0.632 |
| 8 | act2Start | 17 | 20 | Venerate | energyGain | 372 | 5.383 | 716 | 7.466 | 1224 | 9.189 |
| 8 | act2Start | 17 | 20 | Venerate | starGain | 303 | -3.026 | 573 | -2.161 | 947 | -0.499 |
| 9 | act2Start | 17 | 3 | CollisionCourse+1 | draw | 387 | 3.997 | 851 | 6.272 | 1348 | 4.843 |
| 9 | act2Start | 17 | 3 | CollisionCourse+1 | energyGain | 387 | 5.603 | 837 | 7.999 | 1376 | 9.335 |
| 9 | act2Start | 17 | 3 | CollisionCourse+1 | starGain | 387 | 6.437 | 829 | 11.402 | 1360 | 12.88 |
| 9 | act2Start | 17 | 6 | DefendRegent+1 | draw | 365 | 3.31 | 801 | 5.643 | 1274 | 5.144 |
| 9 | act2Start | 17 | 6 | DefendRegent+1 | energyGain | 382 | 7.918 | 836 | 9.468 | 1360 | 10.736 |
| 9 | act2Start | 17 | 6 | DefendRegent+1 | starGain | 374 | 5.94 | 821 | 11.179 | 1360 | 11.623 |
| 9 | act2Start | 17 | 8 | DyingStar | draw | 177 | -0.475 | 205 | 3.53 | 210 | 7.282 |
| 9 | act2Start | 17 | 8 | DyingStar | energyGain | 175 | 8.082 | 204 | 7.861 | 207 | 7.681 |
| 9 | act2Start | 17 | 8 | DyingStar | starGain | 183 | 4.192 | 234 | 8.489 | 241 | 11.283 |
| 9 | act2Start | 17 | 9 | FallingStar | draw | 240 | 3.287 | 483 | 6.041 | 774 | 7.876 |
| 9 | act2Start | 17 | 9 | FallingStar | energyGain | 214 | 8.385 | 467 | 9.732 | 768 | 11.695 |
| 9 | act2Start | 17 | 9 | FallingStar | starGain | 247 | 6.245 | 528 | 11.067 | 888 | 12.291 |
| 9 | act2Start | 17 | 10 | Hegemony | draw | 178 | 2.719 | 314 | 4.419 | 424 | 5.658 |
| 9 | act2Start | 17 | 10 | Hegemony | energyGain | 364 | 11.64 | 796 | 14.002 | 1297 | 17.596 |
| 9 | act2Start | 17 | 10 | Hegemony | starGain | 218 | 7.602 | 476 | 14.592 | 701 | 17.503 |
| 9 | act2Start | 17 | 13 | Reflect | draw | 277 | 3.37 | 424 | 5.075 | 570 | 5.265 |
| 9 | act2Start | 17 | 13 | Reflect | energyGain | 279 | 8.082 | 434 | 9.539 | 586 | 10.451 |
| 9 | act2Start | 17 | 13 | Reflect | starGain | 277 | 4.674 | 446 | 9.726 | 595 | 11.353 |
| 9 | act2Start | 17 | 15 | SpectrumShift+1 | draw | 357 | 3.954 | 376 | 5.324 | 376 | 5.28 |
| 9 | act2Start | 17 | 15 | SpectrumShift+1 | energyGain | 357 | 7.301 | 376 | 7.799 | 376 | 9.282 |
| 9 | act2Start | 17 | 15 | SpectrumShift+1 | starGain | 357 | 6.415 | 376 | 12.601 | 376 | 11.094 |
| 9 | act2Start | 17 | 16 | StrikeRegent | draw | 259 | 2.971 | 495 | 4.006 | 667 | 3.466 |
| 9 | act2Start | 17 | 16 | StrikeRegent | energyGain | 381 | 5.556 | 831 | 5.47 | 1356 | 5.572 |
| 9 | act2Start | 17 | 16 | StrikeRegent | starGain | 324 | 4.967 | 740 | 7.636 | 1209 | 8.438 |
| 9 | act2Start | 17 | 18 | StrikeRegent | draw | 219 | 2.097 | 381 | 6.22 | 526 | 6.821 |
| 9 | act2Start | 17 | 18 | StrikeRegent | energyGain | 381 | 5.432 | 823 | 6.164 | 1361 | 5.809 |
| 9 | act2Start | 17 | 18 | StrikeRegent | starGain | 322 | 3.617 | 738 | 7.274 | 1214 | 7.133 |
| 9 | act2Start | 17 | 20 | Venerate | draw | 314 | 2.173 | 662 | 5.212 | 956 | 6.169 |
| 9 | act2Start | 17 | 20 | Venerate | energyGain | 347 | 10.609 | 737 | 10.18 | 1225 | 11.944 |
| 9 | act2Start | 17 | 20 | Venerate | starGain | 337 | 1.53 | 710 | 8.358 | 1158 | 8.7 |
| 10 | act2Start | 17 | 1 | BigBang | draw | 345 | 5.978 | 400 | 5.916 | 400 | 9.537 |
| 10 | act2Start | 17 | 1 | BigBang | energyGain | 345 | 8.921 | 400 | 8.193 | 400 | 7.181 |
| 10 | act2Start | 17 | 1 | BigBang | starGain | 345 | 3.084 | 400 | 5.202 | 400 | 6.832 |
| 10 | act2Start | 17 | 5 | DefendRegent | draw | 316 | 4.076 | 641 | 2.519 | 1130 | 3.229 |
| 10 | act2Start | 17 | 5 | DefendRegent | energyGain | 389 | 8.186 | 776 | 8.658 | 1340 | 9.704 |
| 10 | act2Start | 17 | 5 | DefendRegent | starGain | 351 | 2.036 | 725 | 2.275 | 1247 | 3.273 |
| 10 | act2Start | 17 | 9 | DyingStar | draw | 352 | 3.627 | 553 | 1.638 | 657 | 1.59 |
| 10 | act2Start | 17 | 9 | DyingStar | energyGain | 408 | 9.117 | 690 | 6.68 | 784 | 3.198 |
| 10 | act2Start | 17 | 9 | DyingStar | starGain | 397 | 2.566 | 708 | 3.333 | 915 | 2.702 |
| 10 | act2Start | 17 | 11 | Glow+1 | draw | 338 | 4.583 | 709 | 3.553 | 1174 | 5.145 |
| 10 | act2Start | 17 | 11 | Glow+1 | energyGain | 387 | 10.904 | 783 | 12.506 | 1333 | 13.914 |
| 10 | act2Start | 17 | 11 | Glow+1 | starGain | 343 | 1.756 | 692 | 4.087 | 1150 | 4.123 |
| 10 | act2Start | 17 | 12 | Havoc+1 | draw | 399 | 6.92 | 785 | 6.001 | 1330 | 7.295 |
| 10 | act2Start | 17 | 12 | Havoc+1 | energyGain | 387 | 7.13 | 741 | 8.402 | 1274 | 8.931 |
| 10 | act2Start | 17 | 12 | Havoc+1 | starGain | 385 | 2.949 | 759 | 4.366 | 1297 | 5.174 |
| 10 | act2Start | 17 | 13 | HiddenCache | draw | 369 | 4.966 | 756 | 4.434 | 1317 | 5.92 |
| 10 | act2Start | 17 | 13 | HiddenCache | energyGain | 379 | 7.796 | 757 | 8.911 | 1297 | 10.451 |
| 10 | act2Start | 17 | 13 | HiddenCache | starGain | 352 | 2.157 | 735 | 3.49 | 1259 | 3.222 |
| 10 | act2Start | 17 | 15 | Quasar+1 | draw | 361 | 5.354 | 678 | 3.322 | 1197 | 4.356 |
| 10 | act2Start | 17 | 15 | Quasar+1 | energyGain | 367 | 11.515 | 679 | 12.294 | 1150 | 12.912 |
| 10 | act2Start | 17 | 15 | Quasar+1 | starGain | 359 | 4.169 | 695 | 4.492 | 1180 | 5.394 |
| 10 | act2Start | 17 | 17 | StrikeRegent | draw | 251 | 4.524 | 469 | 3.9 | 778 | 4.38 |
| 10 | act2Start | 17 | 17 | StrikeRegent | energyGain | 375 | 6.594 | 753 | 7.537 | 1305 | 7.996 |
| 10 | act2Start | 17 | 17 | StrikeRegent | starGain | 301 | 3.297 | 543 | 3.752 | 835 | 5.456 |
| 10 | act2Start | 17 | 18 | StrikeRegent | draw | 221 | 5.426 | 427 | 3.175 | 739 | 5.634 |
| 10 | act2Start | 17 | 18 | StrikeRegent | energyGain | 385 | 6.999 | 757 | 6.851 | 1298 | 7.765 |
| 10 | act2Start | 17 | 18 | StrikeRegent | starGain | 283 | 2.363 | 515 | 2.53 | 829 | 3.981 |
| 10 | act2Start | 17 | 20 | Venerate | draw | 273 | 4.233 | 534 | 2.498 | 865 | 2.249 |
| 10 | act2Start | 17 | 20 | Venerate | energyGain | 377 | 6.063 | 758 | 6.712 | 1312 | 6.539 |
| 10 | act2Start | 17 | 20 | Venerate | starGain | 306 | -1.484 | 592 | 1.685 | 976 | 1.669 |
| 11 | act2Start | 17 | 3 | CloakOfStars | draw | 374 | 6.51 | 704 | 7.04 | 1155 | 6.698 |
| 11 | act2Start | 17 | 3 | CloakOfStars | energyGain | 386 | 7.028 | 703 | 9.569 | 1157 | 11.4 |
| 11 | act2Start | 17 | 3 | CloakOfStars | starGain | 426 | 5.701 | 783 | 11.289 | 1298 | 11.669 |
| 11 | act2Start | 17 | 4 | CollisionCourse | draw | 460 | 4.443 | 904 | 6.838 | 1504 | 6.727 |
| 11 | act2Start | 17 | 4 | CollisionCourse | energyGain | 461 | 6.456 | 908 | 9.288 | 1538 | 10.826 |
| 11 | act2Start | 17 | 4 | CollisionCourse | starGain | 453 | 5.444 | 909 | 11.171 | 1513 | 12.759 |
| 11 | act2Start | 17 | 5 | DefendRegent | draw | 347 | 2.729 | 707 | 5.276 | 1143 | 5.176 |
| 11 | act2Start | 17 | 5 | DefendRegent | energyGain | 449 | 7.341 | 897 | 8.548 | 1496 | 8.509 |
| 11 | act2Start | 17 | 5 | DefendRegent | starGain | 374 | 6.23 | 792 | 9.967 | 1292 | 10.77 |
| 11 | act2Start | 17 | 6 | DefendRegent | draw | 333 | 4.358 | 675 | 4.427 | 1082 | 4.871 |
| 11 | act2Start | 17 | 6 | DefendRegent | energyGain | 443 | 7.674 | 894 | 9.129 | 1499 | 9.807 |
| 11 | act2Start | 17 | 6 | DefendRegent | starGain | 375 | 5.454 | 762 | 10.149 | 1265 | 10.397 |
| 11 | act2Start | 17 | 7 | DefendRegent | draw | 295 | 5.539 | 610 | 5.883 | 1031 | 4.093 |
| 11 | act2Start | 17 | 7 | DefendRegent | energyGain | 442 | 7.02 | 874 | 7.922 | 1471 | 9.732 |
| 11 | act2Start | 17 | 7 | DefendRegent | starGain | 377 | 4.675 | 765 | 9.761 | 1257 | 9.615 |
| 11 | act2Start | 17 | 9 | FallingStar | draw | 341 | 4.196 | 639 | 6.848 | 1006 | 6.889 |
| 11 | act2Start | 17 | 9 | FallingStar | energyGain | 343 | 8.244 | 647 | 12.077 | 1009 | 11.6 |
| 11 | act2Start | 17 | 9 | FallingStar | starGain | 357 | 5.568 | 674 | 9.7 | 1093 | 10.572 |
| 11 | act2Start | 17 | 10 | Glow+1 | draw | 424 | 4.488 | 816 | 4.295 | 1363 | 4.56 |
| 11 | act2Start | 17 | 10 | Glow+1 | energyGain | 440 | 11.349 | 857 | 13.153 | 1433 | 14.404 |
| 11 | act2Start | 17 | 10 | Glow+1 | starGain | 416 | 5.503 | 813 | 11.308 | 1357 | 12.533 |
| 11 | act2Start | 17 | 14 | RefineBlade+1 | draw | 248 | 5.906 | 446 | 11.179 | 723 | 11.596 |
| 11 | act2Start | 17 | 14 | RefineBlade+1 | energyGain | 427 | 10.913 | 892 | 17.246 | 1489 | 24.234 |
| 11 | act2Start | 17 | 14 | RefineBlade+1 | starGain | 220 | 7.073 | 397 | 17.348 | 642 | 19.363 |
| 11 | act2Start | 17 | 17 | StrikeRegent | draw | 256 | 2.459 | 508 | 6.768 | 813 | 7.023 |
| 11 | act2Start | 17 | 17 | StrikeRegent | energyGain | 441 | 5.797 | 896 | 6.568 | 1489 | 7.6 |
| 11 | act2Start | 17 | 17 | StrikeRegent | starGain | 356 | 4.271 | 740 | 8.945 | 1212 | 9.803 |
| 11 | act2Start | 17 | 18 | StrikeRegent | draw | 263 | 3.859 | 481 | 6.066 | 759 | 5.591 |
| 11 | act2Start | 17 | 18 | StrikeRegent | energyGain | 428 | 5.707 | 880 | 7.47 | 1494 | 7.63 |
| 11 | act2Start | 17 | 18 | StrikeRegent | starGain | 358 | 3.513 | 741 | 9.448 | 1210 | 8.809 |
| 12 | act2Start | 17 | 2 | AstralPulse | draw | 396 | 2.328 | 663 | 2.731 | 984 | 2.639 |
| 12 | act2Start | 17 | 2 | AstralPulse | energyGain | 408 | 6.539 | 660 | 6.507 | 991 | 8.549 |
| 12 | act2Start | 17 | 2 | AstralPulse | starGain | 423 | 2.192 | 780 | 7.728 | 1247 | 12.466 |
| 12 | act2Start | 17 | 5 | DefendRegent | draw | 393 | 1.273 | 780 | 4.228 | 1267 | 3.603 |
| 12 | act2Start | 17 | 5 | DefendRegent | energyGain | 444 | 6.566 | 949 | 7.536 | 1644 | 8.183 |
| 12 | act2Start | 17 | 5 | DefendRegent | starGain | 413 | 2.562 | 883 | 7.833 | 1536 | 10.647 |
| 12 | act2Start | 17 | 6 | DefendRegent | draw | 387 | 2.523 | 747 | 3.944 | 1234 | 2.063 |
| 12 | act2Start | 17 | 6 | DefendRegent | energyGain | 440 | 6.935 | 939 | 7.258 | 1619 | 9.539 |
| 12 | act2Start | 17 | 6 | DefendRegent | starGain | 413 | 2.48 | 874 | 7.368 | 1513 | 11.225 |
| 12 | act2Start | 17 | 9 | Discovery+1 | draw | 162 | -2.973 | 321 | -0.739 | 497 | -4.677 |
| 12 | act2Start | 17 | 9 | Discovery+1 | energyGain | 444 | 0.379 | 957 | -0.932 | 1645 | -0.876 |
| 12 | act2Start | 17 | 9 | Discovery+1 | starGain | 100 | 2.1 | 192 | 5.885 | 357 | 4.613 |
| 12 | act2Start | 17 | 10 | FallingStar | draw | 388 | 3.675 | 747 | 3.718 | 1226 | 3.097 |
| 12 | act2Start | 17 | 10 | FallingStar | energyGain | 382 | 7.24 | 734 | 8.541 | 1190 | 11.638 |
| 12 | act2Start | 17 | 10 | FallingStar | starGain | 423 | 2.831 | 798 | 7.83 | 1326 | 12.346 |
| 12 | act2Start | 17 | 11 | Glow+1 | draw | 450 | 4.74 | 858 | 3.267 | 1511 | 3.013 |
| 12 | act2Start | 17 | 11 | Glow+1 | energyGain | 441 | 9.292 | 844 | 10.425 | 1502 | 13.322 |
| 12 | act2Start | 17 | 11 | Glow+1 | starGain | 435 | 2.989 | 834 | 8.325 | 1495 | 14.361 |
| 12 | act2Start | 17 | 12 | Quasar+1 | draw | 414 | 2.418 | 758 | 2.952 | 1218 | 2.476 |
| 12 | act2Start | 17 | 12 | Quasar+1 | energyGain | 445 | 7.937 | 796 | 8.524 | 1280 | 12.034 |
| 12 | act2Start | 17 | 12 | Quasar+1 | starGain | 432 | 3.437 | 832 | 7.623 | 1366 | 13.124 |
| 12 | act2Start | 17 | 15 | StrikeRegent | draw | 323 | 1.184 | 688 | 2.525 | 1216 | 2.913 |
| 12 | act2Start | 17 | 15 | StrikeRegent | energyGain | 436 | 6.304 | 962 | 7.49 | 1650 | 9.535 |
| 12 | act2Start | 17 | 15 | StrikeRegent | starGain | 398 | 1.674 | 875 | 6.61 | 1495 | 11.468 |
| 12 | act2Start | 17 | 16 | StrikeRegent | draw | 301 | 0.638 | 628 | 1.973 | 1102 | 1.783 |
| 12 | act2Start | 17 | 16 | StrikeRegent | energyGain | 445 | 5.874 | 940 | 7.93 | 1655 | 9.356 |
| 12 | act2Start | 17 | 16 | StrikeRegent | starGain | 413 | 1.881 | 877 | 6.769 | 1501 | 10.89 |
| 12 | act2Start | 17 | 18 | StrikeRegent | draw | 276 | 1.028 | 557 | 3.522 | 1004 | 3.298 |
| 12 | act2Start | 17 | 18 | StrikeRegent | energyGain | 441 | 6.043 | 938 | 7.487 | 1630 | 9.351 |
| 12 | act2Start | 17 | 18 | StrikeRegent | starGain | 408 | 1.403 | 876 | 7.281 | 1479 | 11.748 |
| 13 | act2Start | 17 | 1 | Bulwark | draw | 355 | 3.618 | 691 | 7.06 | 1224 | 5.528 |
| 13 | act2Start | 17 | 1 | Bulwark | energyGain | 400 | 8.938 | 828 | 10.13 | 1483 | 13.306 |
| 13 | act2Start | 17 | 1 | Bulwark | starGain | 355 | 3.219 | 692 | 4.371 | 1229 | 4.141 |
| 13 | act2Start | 17 | 2 | CloakOfStars+1 | draw | 387 | 4.554 | 775 | 5.91 | 1380 | 3.732 |
| 13 | act2Start | 17 | 2 | CloakOfStars+1 | energyGain | 373 | 5.606 | 747 | 6.697 | 1362 | 8.435 |
| 13 | act2Start | 17 | 2 | CloakOfStars+1 | starGain | 377 | 2.116 | 764 | 3.519 | 1414 | 3.529 |
| 13 | act2Start | 17 | 3 | Convergence | draw | 170 | 8.247 | 336 | 13.879 | 601 | 17.324 |
| 13 | act2Start | 17 | 3 | Convergence | energyGain | 400 | 8.867 | 837 | 13.325 | 1505 | 15.84 |
| 13 | act2Start | 17 | 3 | Convergence | starGain | 299 | 5.357 | 684 | 10.557 | 1250 | 12.142 |
| 13 | act2Start | 17 | 4 | DefendRegent | draw | 297 | 4.916 | 631 | 5.107 | 1138 | 3.334 |
| 13 | act2Start | 17 | 4 | DefendRegent | energyGain | 400 | 7.191 | 832 | 7.676 | 1515 | 8.337 |
| 13 | act2Start | 17 | 4 | DefendRegent | starGain | 330 | 2.053 | 744 | 2.602 | 1365 | 1.701 |
| 13 | act2Start | 17 | 7 | DefendRegent | draw | 246 | 7.176 | 498 | 7.22 | 890 | 5.947 |
| 13 | act2Start | 17 | 7 | DefendRegent | energyGain | 400 | 6.743 | 827 | 7.955 | 1487 | 8.879 |
| 13 | act2Start | 17 | 7 | DefendRegent | starGain | 341 | 2.631 | 751 | 3.382 | 1369 | 2.158 |
| 13 | act2Start | 17 | 9 | DyingStar | draw | 269 | 3.416 | 311 | 4.496 | 332 | -2.648 |
| 13 | act2Start | 17 | 9 | DyingStar | energyGain | 312 | 7.713 | 390 | 7.247 | 411 | 11.587 |
| 13 | act2Start | 17 | 9 | DyingStar | starGain | 294 | 3.416 | 412 | 5.339 | 456 | 5.434 |
| 13 | act2Start | 17 | 13 | Parry+1 | draw | 400 | 2.947 | 400 | 2.86 | 400 | 2.487 |
| 13 | act2Start | 17 | 13 | Parry+1 | energyGain | 400 | 9.623 | 400 | 12.709 | 400 | 17.364 |
| 13 | act2Start | 17 | 13 | Parry+1 | starGain | 400 | 2.17 | 400 | 5.664 | 400 | 7.464 |
| 13 | act2Start | 17 | 14 | RefineBlade+1 | draw | 246 | 4.192 | 459 | 9.101 | 757 | 14.991 |
| 13 | act2Start | 17 | 14 | RefineBlade+1 | energyGain | 400 | 8.676 | 825 | 13.821 | 1485 | 23.572 |
| 13 | act2Start | 17 | 14 | RefineBlade+1 | starGain | 221 | 3.106 | 397 | 6.68 | 633 | 8.758 |
| 13 | act2Start | 17 | 15 | StrikeRegent | draw | 236 | 3.31 | 478 | 5.357 | 880 | 4.213 |
| 13 | act2Start | 17 | 15 | StrikeRegent | energyGain | 400 | 5.717 | 828 | 7.237 | 1494 | 7.566 |
| 13 | act2Start | 17 | 15 | StrikeRegent | starGain | 299 | 1.203 | 677 | 2.81 | 1248 | 2.831 |
| 13 | act2Start | 17 | 17 | StrikeRegent | draw | 219 | 4.964 | 446 | 4.341 | 796 | 2.936 |
| 13 | act2Start | 17 | 17 | StrikeRegent | energyGain | 400 | 5.399 | 836 | 6.3 | 1494 | 7.797 |
| 13 | act2Start | 17 | 17 | StrikeRegent | starGain | 302 | 1.642 | 681 | 2.438 | 1261 | 2.9 |
| 14 | act2Start | 17 | 2 | CloakOfStars | draw | 403 | 7.895 | 549 | 6.532 | 735 | 4.622 |
| 14 | act2Start | 17 | 2 | CloakOfStars | energyGain | 362 | 7.171 | 486 | 5.606 | 653 | 4.696 |
| 14 | act2Start | 17 | 2 | CloakOfStars | starGain | 404 | 0.66 | 864 | 4.916 | 1491 | 7.159 |
| 14 | act2Start | 17 | 4 | CosmicIndifference | draw | 667 | 4.854 | 1562 | 5.742 | 2824 | 5.867 |
| 14 | act2Start | 17 | 4 | CosmicIndifference | energyGain | 744 | 8.08 | 1633 | 7.173 | 2645 | 7.202 |
| 14 | act2Start | 17 | 4 | CosmicIndifference | starGain | 686 | 1.752 | 1696 | 4.516 | 2974 | 4.339 |
| 14 | act2Start | 17 | 5 | DefendRegent | draw | 308 | 6.721 | 673 | 5.958 | 1154 | 4.339 |
| 14 | act2Start | 17 | 5 | DefendRegent | energyGain | 334 | 8.301 | 705 | 7.209 | 1249 | 7.596 |
| 14 | act2Start | 17 | 5 | DefendRegent | starGain | 302 | 0.872 | 655 | 5.7 | 1141 | 7.38 |
| 14 | act2Start | 17 | 7 | DefendRegent | draw | 295 | 3.989 | 622 | 6.002 | 1099 | 5.589 |
| 14 | act2Start | 17 | 7 | DefendRegent | energyGain | 320 | 8.178 | 682 | 7.86 | 1218 | 8.172 |
| 14 | act2Start | 17 | 7 | DefendRegent | starGain | 305 | 0.606 | 672 | 5.632 | 1159 | 6.287 |
| 14 | act2Start | 17 | 9 | FallingStar | draw | 335 | 7.079 | 492 | 8.323 | 665 | 5.195 |
| 14 | act2Start | 17 | 9 | FallingStar | energyGain | 322 | 9.207 | 453 | 6.436 | 586 | 4.418 |
| 14 | act2Start | 17 | 9 | FallingStar | starGain | 362 | 2.839 | 527 | 7.438 | 701 | 6.151 |
| 14 | act2Start | 17 | 11 | ManifestAuthority+1 | draw | 361 | 1.26 | 771 | 2.57 | 1392 | 3.916 |
| 14 | act2Start | 17 | 11 | ManifestAuthority+1 | energyGain | 368 | 13.476 | 803 | 13.097 | 1429 | 14.534 |
| 14 | act2Start | 17 | 11 | ManifestAuthority+1 | starGain | 364 | 1.874 | 783 | 7.231 | 1397 | 8.052 |
| 14 | act2Start | 17 | 12 | Orbit+1 | draw | 368 | 5.115 | 400 | 4.352 | 400 | -1.96 |
| 14 | act2Start | 17 | 12 | Orbit+1 | energyGain | 368 | 11.278 | 400 | 13.777 | 400 | 16.488 |
| 14 | act2Start | 17 | 12 | Orbit+1 | starGain | 369 | 2.932 | 400 | 11.195 | 400 | 5.567 |
| 14 | act2Start | 17 | 14 | Quasar | draw | 259 | 4.592 | 375 | 3.86 | 500 | 4.026 |
| 14 | act2Start | 17 | 14 | Quasar | energyGain | 279 | 9.276 | 409 | 8.591 | 539 | 7.113 |
| 14 | act2Start | 17 | 14 | Quasar | starGain | 272 | 1.681 | 403 | 7.167 | 545 | 6.825 |
| 14 | act2Start | 17 | 17 | StrikeRegent | draw | 249 | 2.821 | 544 | 3.829 | 941 | 3.787 |
| 14 | act2Start | 17 | 17 | StrikeRegent | energyGain | 315 | 6.78 | 646 | 6.334 | 1151 | 5.391 |
| 14 | act2Start | 17 | 17 | StrikeRegent | starGain | 285 | -0.067 | 621 | 4.869 | 1087 | 5.334 |
| 14 | act2Start | 17 | 18 | StrikeRegent | draw | 222 | 4.906 | 501 | 6.853 | 887 | 4.241 |
| 14 | act2Start | 17 | 18 | StrikeRegent | energyGain | 316 | 6.676 | 665 | 6.107 | 1164 | 5.53 |
| 14 | act2Start | 17 | 18 | StrikeRegent | starGain | 283 | -1.835 | 630 | 3.62 | 1072 | 4.139 |
| 15 | act2Start | 17 | 2 | DefendRegent | draw | 324 | 4.507 | 567 | 4.706 | 891 | 5.149 |
| 15 | act2Start | 17 | 2 | DefendRegent | energyGain | 456 | 6.341 | 897 | 7.103 | 1507 | 7.339 |
| 15 | act2Start | 17 | 2 | DefendRegent | starGain | 392 | 4.015 | 726 | 3.012 | 1124 | 3.416 |
| 15 | act2Start | 17 | 4 | DefendRegent | draw | 287 | 6.8 | 530 | 6.477 | 797 | 7.054 |
| 15 | act2Start | 17 | 4 | DefendRegent | energyGain | 456 | 6.199 | 919 | 7.509 | 1531 | 8.245 |
| 15 | act2Start | 17 | 4 | DefendRegent | starGain | 379 | 4.29 | 729 | 3.599 | 1113 | 3.334 |
| 15 | act2Start | 17 | 5 | DramaticEntrance | draw | 400 | 5.013 | 400 | 7.142 | 400 | 7.689 |
| 15 | act2Start | 17 | 5 | DramaticEntrance | energyGain | 400 | 6.81 | 400 | 6.651 | 400 | 5.543 |
| 15 | act2Start | 17 | 5 | DramaticEntrance | starGain | 400 | 7.516 | 400 | 7.148 | 400 | 9.071 |
| 15 | act2Start | 17 | 8 | GatherLight | draw | 457 | 4.734 | 856 | 6.211 | 1399 | 5.58 |
| 15 | act2Start | 17 | 8 | GatherLight | energyGain | 460 | 8.075 | 906 | 9.286 | 1510 | 10.904 |
| 15 | act2Start | 17 | 8 | GatherLight | starGain | 443 | 6.062 | 854 | 4.469 | 1419 | 5.265 |
| 15 | act2Start | 17 | 11 | Hegemony+1 | draw | 267 | 5.191 | 451 | 8.129 | 592 | 9.711 |
| 15 | act2Start | 17 | 11 | Hegemony+1 | energyGain | 411 | 13.173 | 793 | 16.315 | 1225 | 20.299 |
| 15 | act2Start | 17 | 11 | Hegemony+1 | starGain | 319 | 7.585 | 545 | 9.138 | 788 | 12.64 |
| 15 | act2Start | 17 | 13 | Quasar+1 | draw | 422 | 3.482 | 791 | 4.484 | 1290 | 5.581 |
| 15 | act2Start | 17 | 13 | Quasar+1 | energyGain | 412 | 8.279 | 792 | 9.954 | 1325 | 12.134 |
| 15 | act2Start | 17 | 13 | Quasar+1 | starGain | 416 | 5.22 | 796 | 4.479 | 1325 | 3.116 |
| 15 | act2Start | 17 | 14 | RefineBlade | draw | 176 | 3.364 | 336 | 6.239 | 477 | 8.605 |
| 15 | act2Start | 17 | 14 | RefineBlade | energyGain | 458 | 6.816 | 910 | 8.813 | 1498 | 13.858 |
| 15 | act2Start | 17 | 14 | RefineBlade | starGain | 165 | 8.972 | 302 | 6.453 | 426 | 7.471 |
| 15 | act2Start | 17 | 16 | StrikeRegent | draw | 262 | 5.586 | 480 | 5.517 | 744 | 5.478 |
| 15 | act2Start | 17 | 16 | StrikeRegent | energyGain | 449 | 4.994 | 903 | 6.28 | 1527 | 6.216 |
| 15 | act2Start | 17 | 16 | StrikeRegent | starGain | 373 | 3.095 | 730 | 0.669 | 1118 | 1.39 |
| 15 | act2Start | 17 | 17 | StrikeRegent | draw | 221 | 5.995 | 405 | 7.513 | 638 | 7.057 |
| 15 | act2Start | 17 | 17 | StrikeRegent | energyGain | 455 | 5.664 | 909 | 5.596 | 1526 | 6.657 |
| 15 | act2Start | 17 | 17 | StrikeRegent | starGain | 381 | 2.465 | 735 | 1.365 | 1124 | 2.069 |
| 15 | act2Start | 17 | 18 | Venerate | draw | 398 | 4.141 | 736 | 5.582 | 1137 | 5.506 |
| 15 | act2Start | 17 | 18 | Venerate | energyGain | 458 | 7.749 | 893 | 7.365 | 1512 | 8.437 |
| 15 | act2Start | 17 | 18 | Venerate | starGain | 409 | 2.536 | 755 | 2.638 | 1180 | 3.059 |
| 16 | act2Start | 17 | 1 | Begone | draw | 329 | 5.104 | 764 | 3.699 | 1557 | 3.347 |
| 16 | act2Start | 17 | 1 | Begone | energyGain | 326 | 6.15 | 759 | 7.17 | 1617 | 7.225 |
| 16 | act2Start | 17 | 1 | Begone | starGain | 333 | 1.305 | 814 | 3.979 | 1714 | 5.172 |
| 16 | act2Start | 17 | 2 | CelestialMight+1 | draw | 67 | -0.442 | 122 | -1.236 | 194 | -2.746 |
| 16 | act2Start | 17 | 2 | CelestialMight+1 | energyGain | 194 | 2.373 | 408 | 1.491 | 655 | 1.414 |
| 16 | act2Start | 17 | 2 | CelestialMight+1 | starGain | 68 | 2.535 | 141 | 2.664 | 231 | 1.846 |
| 16 | act2Start | 17 | 3 | CloakOfStars | draw | 481 | 6.425 | 1000 | 5.484 | 1890 | 4.89 |
| 16 | act2Start | 17 | 3 | CloakOfStars | energyGain | 401 | 3.154 | 832 | 3.258 | 1606 | 3.631 |
| 16 | act2Start | 17 | 3 | CloakOfStars | starGain | 440 | 1.463 | 948 | 3.769 | 1852 | 4.193 |
| 16 | act2Start | 17 | 5 | DefendRegent | draw | 350 | 4.689 | 815 | 2.956 | 1530 | 2.258 |
| 16 | act2Start | 17 | 5 | DefendRegent | energyGain | 315 | 5.848 | 758 | 5.865 | 1515 | 6.016 |
| 16 | act2Start | 17 | 5 | DefendRegent | starGain | 344 | 1.492 | 820 | 3.206 | 1619 | 3.815 |
| 16 | act2Start | 17 | 7 | DefendRegent | draw | 352 | 5.835 | 761 | 3.298 | 1438 | 2.656 |
| 16 | act2Start | 17 | 7 | DefendRegent | energyGain | 329 | 5.07 | 788 | 5.344 | 1563 | 5.782 |
| 16 | act2Start | 17 | 7 | DefendRegent | starGain | 363 | 1.089 | 835 | 2.917 | 1643 | 3.355 |
| 16 | act2Start | 17 | 9 | FallingStar | draw | 537 | 6.607 | 1097 | 5.296 | 1894 | 4.984 |
| 16 | act2Start | 17 | 9 | FallingStar | energyGain | 527 | 3.256 | 1081 | 4.073 | 1853 | 4.497 |
| 16 | act2Start | 17 | 9 | FallingStar | starGain | 588 | 1.634 | 1408 | 3.925 | 2469 | 4.299 |
| 16 | act2Start | 17 | 12 | KinglyPunch+1 | draw | 381 | 4.729 | 923 | 3.135 | 1862 | 2.792 |
| 16 | act2Start | 17 | 12 | KinglyPunch+1 | energyGain | 354 | 5.075 | 837 | 5.685 | 1692 | 6.127 |
| 16 | act2Start | 17 | 12 | KinglyPunch+1 | starGain | 387 | 0.534 | 927 | 2.935 | 1864 | 3.718 |
| 16 | act2Start | 17 | 14 | StrikeRegent | draw | 286 | 4.701 | 615 | 2.63 | 1044 | 1.45 |
| 16 | act2Start | 17 | 14 | StrikeRegent | energyGain | 335 | 4.547 | 775 | 4.628 | 1528 | 4.366 |
| 16 | act2Start | 17 | 14 | StrikeRegent | starGain | 353 | 0.335 | 805 | 1.923 | 1575 | 2.185 |
| 16 | act2Start | 17 | 16 | StrikeRegent | draw | 278 | 5.23 | 592 | 2.947 | 1019 | 2.53 |
| 16 | act2Start | 17 | 16 | StrikeRegent | energyGain | 331 | 4.649 | 755 | 4.864 | 1528 | 4.744 |
| 16 | act2Start | 17 | 16 | StrikeRegent | starGain | 352 | 0.648 | 814 | 2.501 | 1624 | 2.332 |
| 16 | act2Start | 17 | 17 | StrikeRegent | draw | 261 | 5.496 | 543 | 4.281 | 944 | 3.111 |
| 16 | act2Start | 17 | 17 | StrikeRegent | energyGain | 323 | 4.7 | 759 | 4.988 | 1544 | 4.766 |
| 16 | act2Start | 17 | 17 | StrikeRegent | starGain | 352 | 0.39 | 792 | 2.629 | 1548 | 2.57 |
| 17 | act2Start | 17 | 2 | DefendRegent | draw | 412 | 5.269 | 830 | 4.916 | 1419 | 5.462 |
| 17 | act2Start | 17 | 2 | DefendRegent | energyGain | 498 | 11.776 | 999 | 12.893 | 1666 | 14.161 |
| 17 | act2Start | 17 | 2 | DefendRegent | starGain | 436 | 4.007 | 914 | 6.238 | 1493 | 6.722 |
| 17 | act2Start | 17 | 3 | DefendRegent | draw | 409 | 4.263 | 819 | 5.136 | 1366 | 5.392 |
| 17 | act2Start | 17 | 3 | DefendRegent | energyGain | 510 | 10.441 | 1005 | 11.936 | 1680 | 13.021 |
| 17 | act2Start | 17 | 3 | DefendRegent | starGain | 444 | 4.204 | 887 | 6.766 | 1481 | 7.672 |
| 17 | act2Start | 17 | 4 | DefendRegent | draw | 392 | 5.453 | 773 | 5.204 | 1310 | 5.377 |
| 17 | act2Start | 17 | 4 | DefendRegent | energyGain | 515 | 11.421 | 998 | 11.941 | 1682 | 13.303 |
| 17 | act2Start | 17 | 4 | DefendRegent | starGain | 432 | 3.517 | 870 | 6.204 | 1457 | 7.461 |
| 17 | act2Start | 17 | 6 | FallingStar | draw | 396 | 5.788 | 619 | 5.873 | 866 | 4.358 |
| 17 | act2Start | 17 | 6 | FallingStar | energyGain | 409 | 12.977 | 683 | 14.238 | 956 | 15.968 |
| 17 | act2Start | 17 | 6 | FallingStar | starGain | 397 | 3.434 | 707 | 6.003 | 1084 | 6.119 |
| 17 | act2Start | 17 | 7 | Fasten+1 | draw | 400 | 3.537 | 400 | 5.12 | 400 | 6.228 |
| 17 | act2Start | 17 | 7 | Fasten+1 | energyGain | 400 | 12.614 | 400 | 16.454 | 400 | 21.36 |
| 17 | act2Start | 17 | 7 | Fasten+1 | starGain | 400 | 4.098 | 400 | 9.518 | 400 | 9.509 |
| 17 | act2Start | 17 | 8 | Glimmer | draw | 299 | 3.806 | 520 | 4.707 | 851 | 2.566 |
| 17 | act2Start | 17 | 8 | Glimmer | energyGain | 1078 | 9.379 | 2634 | 9.729 | 4883 | 10.49 |
| 17 | act2Start | 17 | 8 | Glimmer | starGain | 361 | 2.338 | 692 | 4.494 | 1144 | 4.597 |
| 17 | act2Start | 17 | 11 | ParticleWall | draw | 421 | 5.545 | 633 | 5.901 | 886 | 6.16 |
| 17 | act2Start | 17 | 11 | ParticleWall | energyGain | 444 | 11.196 | 728 | 13.409 | 1033 | 14.875 |
| 17 | act2Start | 17 | 11 | ParticleWall | starGain | 428 | 3.354 | 745 | 6.996 | 1113 | 7.856 |
| 17 | act2Start | 17 | 14 | StrikeRegent | draw | 235 | 5.188 | 427 | 6.031 | 690 | 4.643 |
| 17 | act2Start | 17 | 14 | StrikeRegent | energyGain | 491 | 6.98 | 944 | 8.459 | 1595 | 9.066 |
| 17 | act2Start | 17 | 14 | StrikeRegent | starGain | 306 | 1.646 | 562 | 5.929 | 908 | 4.627 |
| 17 | act2Start | 17 | 15 | StrikeRegent | draw | 234 | 4.947 | 411 | 5.52 | 636 | 2.345 |
| 17 | act2Start | 17 | 15 | StrikeRegent | energyGain | 477 | 7.45 | 951 | 8.374 | 1596 | 8.824 |
| 17 | act2Start | 17 | 15 | StrikeRegent | starGain | 295 | 3.847 | 535 | 5.192 | 835 | 5.513 |
| 17 | act2Start | 17 | 18 | WroughtInWar+1 | draw | 364 | 4.011 | 654 | 4.939 | 1034 | 4.711 |
| 17 | act2Start | 17 | 18 | WroughtInWar+1 | energyGain | 472 | 12.146 | 955 | 13.703 | 1626 | 16.434 |
| 17 | act2Start | 17 | 18 | WroughtInWar+1 | starGain | 353 | 4.224 | 678 | 6.51 | 1117 | 8.221 |
| 18 | act2Start | 17 | 1 | Bulwark+1 | draw | 407 | 2.44 | 919 | 4.09 | 1542 | 0.352 |
| 18 | act2Start | 17 | 1 | Bulwark+1 | energyGain | 462 | 15.995 | 930 | 27.699 | 1585 | 34.921 |
| 18 | act2Start | 17 | 1 | Bulwark+1 | starGain | 391 | 0.55 | 831 | 5.461 | 1373 | 8.308 |
| 18 | act2Start | 17 | 2 | Charge | draw | 109 | 12.972 | 289 | 15.253 | 525 | 17.311 |
| 18 | act2Start | 17 | 2 | Charge | energyGain | 400 | 22.596 | 842 | 20.758 | 1739 | 13.41 |
| 18 | act2Start | 17 | 2 | Charge | starGain | 62 | 0.535 | 215 | 15.598 | 407 | 14.768 |
| 18 | act2Start | 17 | 4 | DefendRegent | draw | 299 | -0.515 | 643 | -1.13 | 1137 | -4.693 |
| 18 | act2Start | 17 | 4 | DefendRegent | energyGain | 422 | 11.1 | 841 | 14.165 | 1484 | 9.183 |
| 18 | act2Start | 17 | 4 | DefendRegent | starGain | 295 | -0.662 | 624 | 4.713 | 1088 | 7.002 |
| 18 | act2Start | 17 | 7 | DefendRegent | draw | 238 | 3.583 | 525 | -0.963 | 928 | -5.549 |
| 18 | act2Start | 17 | 7 | DefendRegent | energyGain | 430 | 11.713 | 828 | 16.466 | 1492 | 9.078 |
| 18 | act2Start | 17 | 7 | DefendRegent | starGain | 291 | 0.238 | 634 | 2.44 | 1104 | 4.331 |
| 18 | act2Start | 17 | 10 | Hegemony+1 | draw | 324 | 11.415 | 821 | 15.479 | 1417 | 17.122 |
| 18 | act2Start | 17 | 10 | Hegemony+1 | energyGain | 655 | 23.955 | 1383 | 28.728 | 2198 | 27.429 |
| 18 | act2Start | 17 | 10 | Hegemony+1 | starGain | 328 | 7.38 | 775 | 10.626 | 1210 | 10.82 |
| 18 | act2Start | 17 | 11 | Orbit+1 | draw | 369 | 6.575 | 393 | 8.018 | 393 | -9.43 |
| 18 | act2Start | 17 | 11 | Orbit+1 | energyGain | 370 | 23.955 | 393 | 40.386 | 393 | 29.86 |
| 18 | act2Start | 17 | 11 | Orbit+1 | starGain | 368 | 1.591 | 393 | 11.299 | 393 | 17.255 |
| 18 | act2Start | 17 | 14 | StrikeRegent | draw | 221 | -1.558 | 466 | -4.707 | 761 | -9.111 |
| 18 | act2Start | 17 | 14 | StrikeRegent | energyGain | 417 | 8.799 | 779 | 14.361 | 1317 | 9.424 |
| 18 | act2Start | 17 | 14 | StrikeRegent | starGain | 271 | -2.711 | 566 | 1.543 | 965 | 4.514 |
| 18 | act2Start | 17 | 15 | StrikeRegent | draw | 208 | -2.025 | 431 | -4.89 | 724 | -7.373 |
| 18 | act2Start | 17 | 15 | StrikeRegent | energyGain | 419 | 8.638 | 779 | 12.986 | 1343 | 9.642 |
| 18 | act2Start | 17 | 15 | StrikeRegent | starGain | 280 | -2.201 | 569 | 1.548 | 995 | 0.432 |
| 18 | act2Start | 17 | 17 | WroughtInWar | draw | 257 | 5.457 | 590 | 5.075 | 1057 | 2.488 |
| 18 | act2Start | 17 | 17 | WroughtInWar | energyGain | 408 | 14.177 | 787 | 24.873 | 1397 | 24.094 |
| 18 | act2Start | 17 | 17 | WroughtInWar | starGain | 327 | 1.218 | 703 | 5.829 | 1252 | 11.7 |
| 18 | act2Start | 17 | 18 | WroughtInWar+1 | draw | 380 | 2.618 | 774 | 4.885 | 1412 | 1.147 |
| 18 | act2Start | 17 | 18 | WroughtInWar+1 | energyGain | 422 | 12.269 | 848 | 20.879 | 1545 | 19.173 |
| 18 | act2Start | 17 | 18 | WroughtInWar+1 | starGain | 361 | -1.608 | 787 | 4.028 | 1390 | 9.441 |
| 19 | act2Start | 17 | 3 | DecisionsDecisions | draw | 33 | 0.776 | 79 | 5.271 | 112 | 7.696 |
| 19 | act2Start | 17 | 3 | DecisionsDecisions | energyGain | 69 | 6.023 | 164 | 6.612 | 224 | 4.123 |
| 19 | act2Start | 17 | 3 | DecisionsDecisions | starGain | 70 | 5.749 | 157 | 8.031 | 216 | 4.819 |
| 19 | act2Start | 17 | 4 | DefendRegent | draw | 561 | 3.169 | 1201 | 4.698 | 2213 | 5.559 |
| 19 | act2Start | 17 | 4 | DefendRegent | energyGain | 644 | 8.531 | 1456 | 10.407 | 2656 | 11.001 |
| 19 | act2Start | 17 | 4 | DefendRegent | starGain | 626 | 1.542 | 1370 | 2.451 | 2538 | 3.916 |
| 19 | act2Start | 17 | 6 | DefendRegent | draw | 512 | 3.323 | 1102 | 4.876 | 2023 | 5.527 |
| 19 | act2Start | 17 | 6 | DefendRegent | energyGain | 641 | 8.736 | 1424 | 10.004 | 2603 | 10.733 |
| 19 | act2Start | 17 | 6 | DefendRegent | starGain | 634 | 1.808 | 1351 | 2.354 | 2517 | 3.616 |
| 19 | act2Start | 17 | 8 | FallingStar | draw | 556 | 3.908 | 1125 | 5.03 | 1996 | 6.102 |
| 19 | act2Start | 17 | 8 | FallingStar | energyGain | 555 | 8.454 | 1213 | 10.108 | 2119 | 10.644 |
| 19 | act2Start | 17 | 8 | FallingStar | starGain | 562 | 0.925 | 1208 | 2.133 | 2229 | 3.344 |
| 19 | act2Start | 17 | 10 | Glimmer+1 | draw | 758 | 1.954 | 1928 | 0.86 | 3731 | -0.214 |
| 19 | act2Start | 17 | 10 | Glimmer+1 | energyGain | 1088 | 9.528 | 2858 | 10.149 | 5369 | 10.849 |
| 19 | act2Start | 17 | 10 | Glimmer+1 | starGain | 685 | 0.938 | 1642 | 2.33 | 3199 | 3.302 |
| 19 | act2Start | 17 | 11 | Glitterstream | draw | 249 | 6.305 | 458 | 6.767 | 779 | 5.301 |
| 19 | act2Start | 17 | 11 | Glitterstream | energyGain | 476 | 8.201 | 1081 | 11.711 | 1964 | 13.59 |
| 19 | act2Start | 17 | 11 | Glitterstream | starGain | 407 | 4.245 | 858 | 7.31 | 1517 | 9.987 |
| 19 | act2Start | 17 | 13 | KinglyKick | draw | 16 | 5.55 | 31 | 2.916 | 51 | 1.725 |
| 19 | act2Start | 17 | 13 | KinglyKick | energyGain | 37 | 7.795 | 114 | 10.933 | 219 | 9.682 |
| 19 | act2Start | 17 | 13 | KinglyKick | starGain | 16 | -2.6 | 33 | -3.248 | 56 | -3.221 |
| 19 | act2Start | 17 | 15 | StrikeRegent | draw | 277 | 4.982 | 532 | 4.921 | 897 | 5.006 |
| 19 | act2Start | 17 | 15 | StrikeRegent | energyGain | 575 | 6.216 | 1292 | 6.462 | 2359 | 5.819 |
| 19 | act2Start | 17 | 15 | StrikeRegent | starGain | 398 | 1.318 | 783 | 1.027 | 1314 | 1.31 |
| 19 | act2Start | 17 | 16 | StrikeRegent | draw | 257 | 6.163 | 496 | 4.983 | 806 | 5.786 |
| 19 | act2Start | 17 | 16 | StrikeRegent | energyGain | 604 | 5.503 | 1324 | 5.922 | 2388 | 5.771 |
| 19 | act2Start | 17 | 16 | StrikeRegent | starGain | 416 | 1.673 | 783 | 1.24 | 1339 | 1.224 |
| 19 | act2Start | 17 | 17 | StrikeRegent | draw | 214 | 6.914 | 399 | 5.674 | 671 | 5.32 |
| 19 | act2Start | 17 | 17 | StrikeRegent | energyGain | 609 | 6.123 | 1318 | 6.087 | 2385 | 5.555 |
| 19 | act2Start | 17 | 17 | StrikeRegent | starGain | 412 | 1.711 | 777 | -0.069 | 1351 | 0.18 |
| 20 | act2Start | 17 | 4 | Charge | draw | 150 | 11.787 | 335 | 17.01 | 604 | 16.312 |
| 20 | act2Start | 17 | 4 | Charge | energyGain | 451 | 21.765 | 910 | 22.755 | 1707 | 23.69 |
| 20 | act2Start | 17 | 4 | Charge | starGain | 85 | -0.113 | 211 | 1.896 | 388 | 3.828 |
| 20 | act2Start | 17 | 6 | DefendRegent | draw | 348 | 3.469 | 716 | 6.655 | 1252 | 6.549 |
| 20 | act2Start | 17 | 6 | DefendRegent | energyGain | 448 | 9.017 | 885 | 12.56 | 1506 | 13.349 |
| 20 | act2Start | 17 | 6 | DefendRegent | starGain | 377 | 0.395 | 762 | 1.585 | 1315 | 1.503 |
| 20 | act2Start | 17 | 9 | DefendRegent | draw | 288 | 5.4 | 604 | 8.587 | 1097 | 7.91 |
| 20 | act2Start | 17 | 9 | DefendRegent | energyGain | 451 | 8.216 | 882 | 10.301 | 1530 | 11.981 |
| 20 | act2Start | 17 | 9 | DefendRegent | starGain | 386 | 1.141 | 782 | 1.193 | 1342 | 1.636 |
| 20 | act2Start | 17 | 10 | Equilibrium | draw | 318 | 5.535 | 653 | 7.961 | 1130 | 8.504 |
| 20 | act2Start | 17 | 10 | Equilibrium | energyGain | 435 | 11.685 | 880 | 12.112 | 1507 | 11.86 |
| 20 | act2Start | 17 | 10 | Equilibrium | starGain | 350 | 1.339 | 730 | 0.996 | 1263 | -0.057 |
| 20 | act2Start | 17 | 11 | FallingStar | draw | 460 | 5.806 | 882 | 8.951 | 1413 | 10.157 |
| 20 | act2Start | 17 | 11 | FallingStar | energyGain | 444 | 11.702 | 880 | 13.325 | 1378 | 13.622 |
| 20 | act2Start | 17 | 11 | FallingStar | starGain | 447 | -0.013 | 895 | 0.704 | 1489 | 1.683 |
| 20 | act2Start | 17 | 14 | ManifestAuthority+1 | draw | 456 | 4.986 | 899 | 5.131 | 1512 | 5.604 |
| 20 | act2Start | 17 | 14 | ManifestAuthority+1 | energyGain | 437 | 12.999 | 891 | 16.101 | 1527 | 18.133 |
| 20 | act2Start | 17 | 14 | ManifestAuthority+1 | starGain | 438 | -0.098 | 880 | 0.198 | 1503 | 2.571 |
| 20 | act2Start | 17 | 15 | Orbit+1 | draw | 394 | 4.692 | 395 | 8.675 | 396 | 14.323 |
| 20 | act2Start | 17 | 15 | Orbit+1 | energyGain | 396 | 12.153 | 396 | 19.34 | 396 | 22.7 |
| 20 | act2Start | 17 | 15 | Orbit+1 | starGain | 396 | 0.959 | 396 | 2.277 | 396 | 1.209 |
| 20 | act2Start | 17 | 16 | StrikeRegent | draw | 238 | 7.022 | 509 | 10.143 | 887 | 11.862 |
| 20 | act2Start | 17 | 16 | StrikeRegent | energyGain | 451 | 8.612 | 867 | 10.373 | 1421 | 12.474 |
| 20 | act2Start | 17 | 16 | StrikeRegent | starGain | 369 | -0.707 | 738 | -1.112 | 1254 | 0.029 |
| 20 | act2Start | 17 | 17 | StrikeRegent | draw | 220 | 3.085 | 472 | 7.699 | 852 | 6.254 |
| 20 | act2Start | 17 | 17 | StrikeRegent | energyGain | 447 | 7.383 | 872 | 11.297 | 1491 | 13.499 |
| 20 | act2Start | 17 | 17 | StrikeRegent | starGain | 375 | -0.897 | 740 | -1.252 | 1250 | 1.271 |
| 20 | act2Start | 17 | 18 | Venerate | draw | 358 | 3.126 | 676 | 7.745 | 1078 | 9.845 |
| 20 | act2Start | 17 | 18 | Venerate | energyGain | 432 | 7.481 | 832 | 10.045 | 1298 | 11.843 |
| 20 | act2Start | 17 | 18 | Venerate | starGain | 373 | -1.64 | 701 | -2.397 | 1145 | -1.683 |
| 21 | act2Start | 17 | 2 | Conqueror | draw | 292 | 5.029 | 527 | 7.054 | 845 | 11.992 |
| 21 | act2Start | 17 | 2 | Conqueror | energyGain | 452 | 6.496 | 923 | 7.46 | 1570 | 12.803 |
| 21 | act2Start | 17 | 2 | Conqueror | starGain | 348 | 2.792 | 610 | 2.799 | 960 | 8.869 |
| 21 | act2Start | 17 | 3 | DecisionsDecisions | draw | 50 | -1.32 | 110 | 2.687 | 206 | 8.515 |
| 21 | act2Start | 17 | 3 | DecisionsDecisions | energyGain | 52 | 9.538 | 119 | 10.555 | 215 | 17.226 |
| 21 | act2Start | 17 | 3 | DecisionsDecisions | starGain | 64 | 12.125 | 139 | 7.983 | 224 | 16.571 |
| 21 | act2Start | 17 | 5 | DefendRegent | draw | 375 | 2.365 | 789 | 3.281 | 1444 | 7.796 |
| 21 | act2Start | 17 | 5 | DefendRegent | energyGain | 473 | 7.412 | 993 | 8.578 | 1829 | 8.972 |
| 21 | act2Start | 17 | 5 | DefendRegent | starGain | 441 | 3.714 | 927 | 5.035 | 1716 | 6.444 |
| 21 | act2Start | 17 | 6 | DefendRegent | draw | 333 | 4.443 | 714 | 7.848 | 1322 | 12.187 |
| 21 | act2Start | 17 | 6 | DefendRegent | energyGain | 480 | 6.273 | 1005 | 7.707 | 1840 | 8.767 |
| 21 | act2Start | 17 | 6 | DefendRegent | starGain | 439 | 2.642 | 928 | 4.934 | 1702 | 4.905 |
| 21 | act2Start | 17 | 8 | FallingStar | draw | 422 | 6.502 | 900 | 8.685 | 1699 | 13.098 |
| 21 | act2Start | 17 | 8 | FallingStar | energyGain | 409 | 7.826 | 881 | 9.778 | 1676 | 11.682 |
| 21 | act2Start | 17 | 8 | FallingStar | starGain | 443 | 6.237 | 922 | 7.044 | 1720 | 12.242 |
| 21 | act2Start | 17 | 9 | Glow+1 | draw | 454 | 2.732 | 924 | 4.791 | 1729 | 9.72 |
| 21 | act2Start | 17 | 9 | Glow+1 | energyGain | 475 | 9.012 | 952 | 11.364 | 1785 | 15.885 |
| 21 | act2Start | 17 | 9 | Glow+1 | starGain | 479 | 3.228 | 982 | 3.305 | 1808 | 8.818 |
| 21 | act2Start | 17 | 10 | Hegemony+1 | draw | 386 | 3.138 | 779 | 3.661 | 1400 | 6.787 |
| 21 | act2Start | 17 | 10 | Hegemony+1 | energyGain | 434 | 13.112 | 968 | 13.08 | 1788 | 14.179 |
| 21 | act2Start | 17 | 10 | Hegemony+1 | starGain | 431 | 8.535 | 895 | 8.751 | 1537 | 15.066 |
| 21 | act2Start | 17 | 11 | Peck | draw | 179 | 5.106 | 398 | 4.547 | 724 | 9.894 |
| 21 | act2Start | 17 | 11 | Peck | energyGain | 456 | 2.143 | 938 | 3.652 | 1583 | 3.982 |
| 21 | act2Start | 17 | 11 | Peck | starGain | 234 | 1.703 | 481 | 4.963 | 818 | 10.947 |
| 21 | act2Start | 17 | 14 | StrikeRegent | draw | 287 | 6.213 | 639 | 5.469 | 1183 | 6.818 |
| 21 | act2Start | 17 | 14 | StrikeRegent | energyGain | 471 | 5.299 | 965 | 5.857 | 1736 | 8.284 |
| 21 | act2Start | 17 | 14 | StrikeRegent | starGain | 414 | 2.278 | 887 | 2.571 | 1589 | 5.668 |
| 21 | act2Start | 17 | 17 | TheSmith+1 | draw | 119 | 8.279 | 223 | 10.879 | 336 | 24.676 |
| 21 | act2Start | 17 | 17 | TheSmith+1 | energyGain | 217 | 12.334 | 344 | 34.397 | 500 | 55.399 |
| 21 | act2Start | 17 | 17 | TheSmith+1 | starGain | 138 | 15.965 | 254 | 24.002 | 382 | 38.489 |
| 22 | act2Start | 17 | 2 | CloakOfStars+1 | draw | 417 | 6.014 | 700 | 3.655 | 1088 | 3.485 |
| 22 | act2Start | 17 | 2 | CloakOfStars+1 | energyGain | 415 | 7.283 | 709 | 7.139 | 1108 | 7.407 |
| 22 | act2Start | 17 | 2 | CloakOfStars+1 | starGain | 425 | 0.069 | 840 | 4.715 | 1371 | 7.193 |
| 22 | act2Start | 17 | 4 | DefendRegent | draw | 286 | 4.806 | 576 | 2.542 | 1031 | 2.416 |
| 22 | act2Start | 17 | 4 | DefendRegent | energyGain | 403 | 8.221 | 807 | 7.454 | 1358 | 7.414 |
| 22 | act2Start | 17 | 4 | DefendRegent | starGain | 340 | 0.245 | 723 | 3.998 | 1254 | 7.175 |
| 22 | act2Start | 17 | 5 | DefendRegent | draw | 293 | 5.02 | 601 | 2.362 | 1023 | 2.2 |
| 22 | act2Start | 17 | 5 | DefendRegent | energyGain | 408 | 7.7 | 804 | 7.144 | 1348 | 7.148 |
| 22 | act2Start | 17 | 5 | DefendRegent | starGain | 358 | 0.027 | 748 | 3.771 | 1267 | 6.837 |
| 22 | act2Start | 17 | 7 | DefendRegent | draw | 246 | 5.104 | 481 | 3.557 | 858 | 3.333 |
| 22 | act2Start | 17 | 7 | DefendRegent | energyGain | 415 | 8.075 | 819 | 7.74 | 1381 | 8.121 |
| 22 | act2Start | 17 | 7 | DefendRegent | starGain | 343 | 0.194 | 737 | 3.943 | 1255 | 7.238 |
| 22 | act2Start | 17 | 8 | FallingStar | draw | 411 | 6.267 | 654 | 3.744 | 987 | 4.175 |
| 22 | act2Start | 17 | 8 | FallingStar | energyGain | 405 | 8.721 | 667 | 9.126 | 996 | 8.978 |
| 22 | act2Start | 17 | 8 | FallingStar | starGain | 405 | 0.07 | 734 | 5.411 | 1154 | 8.085 |
| 22 | act2Start | 17 | 13 | RefineBlade+1 | draw | 262 | 7.344 | 471 | 4.966 | 754 | 6.703 |
| 22 | act2Start | 17 | 13 | RefineBlade+1 | energyGain | 400 | 13.134 | 774 | 12.238 | 1318 | 17.112 |
| 22 | act2Start | 17 | 13 | RefineBlade+1 | starGain | 297 | 2.901 | 505 | 10.183 | 742 | 13.699 |
| 22 | act2Start | 17 | 14 | StrikeRegent | draw | 235 | 3.556 | 469 | 1.321 | 793 | 2.796 |
| 22 | act2Start | 17 | 14 | StrikeRegent | energyGain | 412 | 5.948 | 805 | 5.671 | 1363 | 6.666 |
| 22 | act2Start | 17 | 14 | StrikeRegent | starGain | 341 | -2.683 | 709 | 2.676 | 1218 | 5.821 |
| 22 | act2Start | 17 | 15 | StrikeRegent | draw | 215 | 5.864 | 421 | 2.284 | 753 | 2.692 |
| 22 | act2Start | 17 | 15 | StrikeRegent | energyGain | 412 | 5.86 | 819 | 5.188 | 1357 | 6.236 |
| 22 | act2Start | 17 | 15 | StrikeRegent | starGain | 345 | -1.411 | 711 | 2.682 | 1216 | 5.183 |
| 22 | act2Start | 17 | 16 | StrikeRegent | draw | 207 | 4.537 | 415 | 2.092 | 693 | 3.3 |
| 22 | act2Start | 17 | 16 | StrikeRegent | energyGain | 412 | 6.178 | 801 | 5.93 | 1363 | 6.411 |
| 22 | act2Start | 17 | 16 | StrikeRegent | starGain | 339 | -1.388 | 705 | 2.902 | 1208 | 5.74 |
| 22 | act2Start | 17 | 17 | StrikeRegent | draw | 214 | 4.664 | 399 | 2.809 | 680 | 3.63 |
| 22 | act2Start | 17 | 17 | StrikeRegent | energyGain | 416 | 6.029 | 813 | 4.994 | 1346 | 6.296 |
| 22 | act2Start | 17 | 17 | StrikeRegent | starGain | 338 | -1.82 | 697 | 2.831 | 1210 | 4.958 |
| 23 | act2Start | 17 | 1 | Bulwark+1 | draw | 417 | 1.336 | 791 | 4.597 | 990 | 3.024 |
| 23 | act2Start | 17 | 1 | Bulwark+1 | energyGain | 467 | 11.252 | 999 | 12.945 | 1784 | 4.694 |
| 23 | act2Start | 17 | 1 | Bulwark+1 | starGain | 417 | 1.043 | 773 | 5.031 | 1047 | 7.458 |
| 23 | act2Start | 17 | 3 | ChildOfTheStars | draw | 361 | 3.217 | 393 | 4.542 | 393 | 1.041 |
| 23 | act2Start | 17 | 3 | ChildOfTheStars | energyGain | 361 | 10.57 | 393 | 15.26 | 393 | 15.719 |
| 23 | act2Start | 17 | 3 | ChildOfTheStars | starGain | 361 | 2.659 | 393 | 10.568 | 393 | 16.356 |
| 23 | act2Start | 17 | 5 | CosmicIndifference | draw | 754 | 2.233 | 2039 | 2.465 | 4265 | 0.311 |
| 23 | act2Start | 17 | 5 | CosmicIndifference | energyGain | 1049 | 9.178 | 2618 | 13.146 | 4940 | 13.209 |
| 23 | act2Start | 17 | 5 | CosmicIndifference | starGain | 720 | 1.344 | 1920 | 3.348 | 4290 | 5.68 |
| 23 | act2Start | 17 | 8 | DefendRegent | draw | 227 | 2.4 | 378 | 1.976 | 511 | -6.41 |
| 23 | act2Start | 17 | 8 | DefendRegent | energyGain | 374 | 7.712 | 753 | 8.618 | 1240 | 7.434 |
| 23 | act2Start | 17 | 8 | DefendRegent | starGain | 311 | 0.302 | 734 | -2.796 | 1313 | -17.567 |
| 23 | act2Start | 17 | 11 | HeavenlyDrill+1 | draw | 0 | - | 0 | - | 0 | - |
| 23 | act2Start | 17 | 11 | HeavenlyDrill+1 | energyGain | 0 | - | 0 | - | 0 | - |
| 23 | act2Start | 17 | 11 | HeavenlyDrill+1 | starGain | 0 | - | 0 | - | 0 | - |
| 23 | act2Start | 17 | 12 | Parry | draw | 365 | 3.565 | 400 | 7.893 | 400 | 3.846 |
| 23 | act2Start | 17 | 12 | Parry | energyGain | 365 | 11.779 | 400 | 15.84 | 400 | 19.373 |
| 23 | act2Start | 17 | 12 | Parry | starGain | 365 | 2.184 | 400 | 10.496 | 400 | 18.127 |
| 23 | act2Start | 17 | 14 | StrikeRegent | draw | 194 | 2.32 | 326 | 3.842 | 434 | -8.535 |
| 23 | act2Start | 17 | 14 | StrikeRegent | energyGain | 380 | 5.917 | 746 | 7.094 | 1236 | 5.951 |
| 23 | act2Start | 17 | 14 | StrikeRegent | starGain | 314 | -0.692 | 706 | -4.089 | 1294 | -19.609 |
| 23 | act2Start | 17 | 15 | StrikeRegent | draw | 189 | 1.788 | 307 | 3.247 | 404 | -11.285 |
| 23 | act2Start | 17 | 15 | StrikeRegent | energyGain | 378 | 5.984 | 756 | 6.15 | 1252 | 5.668 |
| 23 | act2Start | 17 | 15 | StrikeRegent | starGain | 319 | -1.221 | 722 | -4.126 | 1288 | -19.17 |
| 23 | act2Start | 17 | 17 | StrikeRegent+1 | draw | 303 | 1.518 | 515 | 1.154 | 661 | -6.473 |
| 23 | act2Start | 17 | 17 | StrikeRegent+1 | energyGain | 381 | 8.554 | 769 | 9.908 | 1235 | 11.355 |
| 23 | act2Start | 17 | 17 | StrikeRegent+1 | starGain | 346 | 1.331 | 752 | -1.901 | 1363 | -16.731 |
| 23 | act2Start | 17 | 18 | Venerate | draw | 288 | 0.918 | 516 | 6.636 | 730 | -4.373 |
| 23 | act2Start | 17 | 18 | Venerate | energyGain | 362 | 5.807 | 734 | 8.769 | 1205 | 10.202 |
| 23 | act2Start | 17 | 18 | Venerate | starGain | 310 | -1.956 | 692 | -4.99 | 1246 | -20.502 |
| 24 | act2Start | 17 | 1 | Charge+1 | draw | 101 | 15.489 | 225 | 14.151 | 414 | 13.996 |
| 24 | act2Start | 17 | 1 | Charge+1 | energyGain | 413 | 23.958 | 855 | 23.076 | 1666 | 23.661 |
| 24 | act2Start | 17 | 1 | Charge+1 | starGain | 58 | 12.531 | 165 | 10.516 | 312 | 6.417 |
| 24 | act2Start | 17 | 4 | DefendRegent | draw | 241 | 4.84 | 516 | 6.989 | 867 | 7.935 |
| 24 | act2Start | 17 | 4 | DefendRegent | energyGain | 420 | 9.149 | 856 | 11.025 | 1479 | 13.822 |
| 24 | act2Start | 17 | 4 | DefendRegent | starGain | 267 | 1.97 | 547 | 4.48 | 959 | 6.941 |
| 24 | act2Start | 17 | 5 | DefendRegent | draw | 227 | 5.981 | 482 | 6.474 | 825 | 8.124 |
| 24 | act2Start | 17 | 5 | DefendRegent | energyGain | 436 | 9.778 | 886 | 10.628 | 1517 | 13.355 |
| 24 | act2Start | 17 | 5 | DefendRegent | starGain | 260 | 1.597 | 547 | 3.893 | 948 | 4.816 |
| 24 | act2Start | 17 | 6 | DefendRegent | draw | 205 | 5.165 | 457 | 7.739 | 791 | 7.786 |
| 24 | act2Start | 17 | 6 | DefendRegent | energyGain | 415 | 9.209 | 844 | 10.581 | 1464 | 12.216 |
| 24 | act2Start | 17 | 6 | DefendRegent | starGain | 251 | 0.982 | 551 | 4.258 | 958 | 6.069 |
| 24 | act2Start | 17 | 8 | FallingStar | draw | 456 | 4.347 | 838 | 4.513 | 1366 | 6.29 |
| 24 | act2Start | 17 | 8 | FallingStar | energyGain | 453 | 12.825 | 799 | 14.474 | 1278 | 18.506 |
| 24 | act2Start | 17 | 8 | FallingStar | starGain | 472 | 1.903 | 924 | 4.473 | 1522 | 5.782 |
| 24 | act2Start | 17 | 9 | GatherLight | draw | 358 | 5.163 | 746 | 5.445 | 1273 | 5.252 |
| 24 | act2Start | 17 | 9 | GatherLight | energyGain | 424 | 12.838 | 871 | 11.688 | 1522 | 14.657 |
| 24 | act2Start | 17 | 9 | GatherLight | starGain | 366 | 0.636 | 778 | 3.824 | 1306 | 6.215 |
| 24 | act2Start | 17 | 11 | ParticleWall | draw | 425 | 4.733 | 770 | 5.32 | 1217 | 6.139 |
| 24 | act2Start | 17 | 11 | ParticleWall | energyGain | 415 | 12.98 | 757 | 13.185 | 1209 | 16.226 |
| 24 | act2Start | 17 | 11 | ParticleWall | starGain | 424 | 2.489 | 817 | 3.726 | 1365 | 5.097 |
| 24 | act2Start | 17 | 14 | StrikeRegent | draw | 203 | 4.108 | 398 | 6.665 | 676 | 6.017 |
| 24 | act2Start | 17 | 14 | StrikeRegent | energyGain | 416 | 8.362 | 843 | 8.728 | 1461 | 11.005 |
| 24 | act2Start | 17 | 14 | StrikeRegent | starGain | 247 | -0.523 | 529 | 1.948 | 917 | 4.663 |
| 24 | act2Start | 17 | 16 | SwordSage | draw | 379 | 2.304 | 392 | 4.76 | 395 | 5.561 |
| 24 | act2Start | 17 | 16 | SwordSage | energyGain | 383 | 14.328 | 395 | 17.106 | 396 | 21.894 |
| 24 | act2Start | 17 | 16 | SwordSage | starGain | 378 | 2.226 | 391 | 6.197 | 395 | 8.933 |
| 24 | act2Start | 17 | 17 | Venerate+1 | draw | 335 | 6.474 | 671 | 7.549 | 1130 | 7.979 |
| 24 | act2Start | 17 | 17 | Venerate+1 | energyGain | 421 | 9.276 | 825 | 12.445 | 1387 | 15.193 |
| 24 | act2Start | 17 | 17 | Venerate+1 | starGain | 351 | 0.234 | 708 | 2.597 | 1187 | 4.139 |
| 25 | act2Start | 17 | 0 | Alchemize+1 | draw | 257 | 2.83 | 257 | 7.186 | 257 | 3.844 |
| 25 | act2Start | 17 | 0 | Alchemize+1 | energyGain | 257 | 4.238 | 257 | 4.163 | 257 | 4.111 |
| 25 | act2Start | 17 | 0 | Alchemize+1 | starGain | 257 | 1.734 | 257 | 8.956 | 257 | 8.697 |
| 25 | act2Start | 17 | 2 | CloakOfStars | draw | 500 | 4.166 | 864 | 5.217 | 1284 | 3.678 |
| 25 | act2Start | 17 | 2 | CloakOfStars | energyGain | 488 | 6.248 | 845 | 6.35 | 1264 | 6.548 |
| 25 | act2Start | 17 | 2 | CloakOfStars | starGain | 493 | 0.37 | 918 | 5.234 | 1391 | 7.778 |
| 25 | act2Start | 17 | 4 | CrashLanding | draw | 515 | 1.516 | 934 | 1.267 | 1580 | 2.858 |
| 25 | act2Start | 17 | 4 | CrashLanding | energyGain | 503 | 6.247 | 911 | 4.122 | 1530 | 5.706 |
| 25 | act2Start | 17 | 4 | CrashLanding | starGain | 489 | 0.589 | 911 | 5.76 | 1498 | 7.448 |
| 25 | act2Start | 17 | 6 | DefendRegent | draw | 430 | 7.709 | 812 | 4.59 | 1335 | 4.349 |
| 25 | act2Start | 17 | 6 | DefendRegent | energyGain | 481 | 4.794 | 917 | 4.317 | 1473 | 4.928 |
| 25 | act2Start | 17 | 6 | DefendRegent | starGain | 486 | 1.374 | 904 | 5.47 | 1450 | 7.169 |
| 25 | act2Start | 17 | 7 | DefendRegent | draw | 435 | 5.664 | 809 | 6.266 | 1339 | 3.407 |
| 25 | act2Start | 17 | 7 | DefendRegent | energyGain | 499 | 5.424 | 940 | 4.674 | 1517 | 5.106 |
| 25 | act2Start | 17 | 7 | DefendRegent | starGain | 488 | 1.495 | 905 | 4.968 | 1474 | 6.816 |
| 25 | act2Start | 17 | 8 | DefendRegent | draw | 396 | 8.114 | 776 | 4.349 | 1297 | 3.472 |
| 25 | act2Start | 17 | 8 | DefendRegent | energyGain | 503 | 6.874 | 942 | 4.972 | 1525 | 4.765 |
| 25 | act2Start | 17 | 8 | DefendRegent | starGain | 488 | 1.523 | 922 | 4.997 | 1490 | 8.456 |
| 25 | act2Start | 17 | 10 | GatherLight | draw | 517 | 4.77 | 921 | 5.568 | 1503 | 3.747 |
| 25 | act2Start | 17 | 10 | GatherLight | energyGain | 506 | 5.507 | 919 | 4.895 | 1521 | 5.15 |
| 25 | act2Start | 17 | 10 | GatherLight | starGain | 507 | 1.497 | 915 | 5.001 | 1502 | 7.109 |
| 25 | act2Start | 17 | 12 | PillarOfCreation+1 | draw | 400 | 4.357 | 400 | 2.387 | 400 | 5.06 |
| 25 | act2Start | 17 | 12 | PillarOfCreation+1 | energyGain | 400 | 6.302 | 400 | 7.628 | 400 | 7.004 |
| 25 | act2Start | 17 | 12 | PillarOfCreation+1 | starGain | 400 | 1.45 | 400 | 8.436 | 400 | 8.088 |
| 25 | act2Start | 17 | 15 | StrikeRegent | draw | 343 | 5.478 | 639 | 5.365 | 1055 | 3.939 |
| 25 | act2Start | 17 | 15 | StrikeRegent | energyGain | 468 | 5.801 | 853 | 4.94 | 1383 | 4.696 |
| 25 | act2Start | 17 | 15 | StrikeRegent | starGain | 459 | 0.5 | 864 | 4.743 | 1386 | 6.848 |
| 25 | act2Start | 17 | 17 | Venerate | draw | 175 | 4.921 | 175 | -2.629 | 175 | 1.746 |
| 25 | act2Start | 17 | 17 | Venerate | energyGain | 190 | 3.446 | 190 | 4.579 | 190 | 6.48 |
| 25 | act2Start | 17 | 17 | Venerate | starGain | 184 | 0.389 | 184 | 5.557 | 184 | 8.637 |
| 26 | act2Start | 17 | 1 | BigBang | draw | 400 | 7.989 | 400 | 11.581 | 400 | 15.672 |
| 26 | act2Start | 17 | 1 | BigBang | energyGain | 400 | 7.457 | 400 | 5.591 | 400 | 7.285 |
| 26 | act2Start | 17 | 1 | BigBang | starGain | 400 | 0.056 | 400 | 0.129 | 400 | 0.028 |
| 26 | act2Start | 17 | 2 | ChildOfTheStars+1 | draw | 400 | 10.902 | 400 | 11.79 | 400 | 14.893 |
| 26 | act2Start | 17 | 2 | ChildOfTheStars+1 | energyGain | 400 | 6.25 | 400 | 6.734 | 400 | 6.158 |
| 26 | act2Start | 17 | 2 | ChildOfTheStars+1 | starGain | 400 | 0.599 | 400 | 1.31 | 400 | 0.508 |
| 26 | act2Start | 17 | 5 | DefendRegent | draw | 412 | 8.782 | 737 | 11.037 | 1137 | 13.207 |
| 26 | act2Start | 17 | 5 | DefendRegent | energyGain | 541 | 6.404 | 1031 | 7.262 | 1752 | 7.216 |
| 26 | act2Start | 17 | 5 | DefendRegent | starGain | 494 | 0.624 | 990 | 1.238 | 1604 | 1.691 |
| 26 | act2Start | 17 | 6 | DefendRegent | draw | 363 | 10.89 | 669 | 11.065 | 1058 | 13.07 |
| 26 | act2Start | 17 | 6 | DefendRegent | energyGain | 534 | 6.454 | 1046 | 6.78 | 1774 | 7.087 |
| 26 | act2Start | 17 | 6 | DefendRegent | starGain | 493 | 1.097 | 987 | 0.655 | 1571 | 1.056 |
| 26 | act2Start | 17 | 7 | FallingStar | draw | 563 | 8.596 | 1089 | 7.902 | 1809 | 9.384 |
| 26 | act2Start | 17 | 7 | FallingStar | energyGain | 506 | 7.302 | 1027 | 7.792 | 1748 | 8.411 |
| 26 | act2Start | 17 | 7 | FallingStar | starGain | 516 | 0.443 | 1040 | 0.628 | 1753 | 0.745 |
| 26 | act2Start | 17 | 8 | Glow+1 | draw | 529 | 8.408 | 996 | 8.618 | 1638 | 9.614 |
| 26 | act2Start | 17 | 8 | Glow+1 | energyGain | 513 | 7.135 | 998 | 7.03 | 1655 | 7.65 |
| 26 | act2Start | 17 | 8 | Glow+1 | starGain | 518 | -0.249 | 984 | -0.08 | 1633 | 0.051 |
| 26 | act2Start | 17 | 9 | HiddenCache | draw | 563 | 9.922 | 1072 | 9.477 | 1773 | 9.608 |
| 26 | act2Start | 17 | 9 | HiddenCache | energyGain | 521 | 6.278 | 1050 | 5.919 | 1753 | 6.165 |
| 26 | act2Start | 17 | 9 | HiddenCache | starGain | 519 | 0.056 | 1039 | 0.012 | 1692 | 0.017 |
| 26 | act2Start | 17 | 10 | KinglyKick | draw | 8 | 29.65 | 8 | 14.75 | 8 | 40.65 |
| 26 | act2Start | 17 | 10 | KinglyKick | energyGain | 77 | 7.709 | 77 | 10.462 | 77 | 11.522 |
| 26 | act2Start | 17 | 10 | KinglyKick | starGain | 20 | -5.8 | 20 | -3.34 | 20 | 5.1 |
| 26 | act2Start | 17 | 14 | StrikeRegent | draw | 326 | 10.281 | 628 | 10.115 | 1006 | 11.079 |
| 26 | act2Start | 17 | 14 | StrikeRegent | energyGain | 529 | 5.257 | 1043 | 6.001 | 1752 | 6.204 |
| 26 | act2Start | 17 | 14 | StrikeRegent | starGain | 480 | 0.059 | 977 | 0.038 | 1603 | 1.032 |
| 26 | act2Start | 17 | 15 | StrikeRegent | draw | 321 | 8.795 | 611 | 11.781 | 920 | 13.334 |
| 26 | act2Start | 17 | 15 | StrikeRegent | energyGain | 522 | 5.756 | 1042 | 6.095 | 1774 | 6.29 |
| 26 | act2Start | 17 | 15 | StrikeRegent | starGain | 502 | -0.078 | 980 | 0.565 | 1581 | 1.212 |
| 27 | act2Start | 17 | 2 | CosmicIndifference | draw | 626 | 5.366 | 1261 | 2.626 | 2087 | 1.386 |
| 27 | act2Start | 17 | 2 | CosmicIndifference | energyGain | 813 | 6.33 | 1672 | 6.171 | 2605 | 6.114 |
| 27 | act2Start | 17 | 2 | CosmicIndifference | starGain | 645 | 6.033 | 1293 | 5.078 | 2066 | 5.129 |
| 27 | act2Start | 17 | 4 | DefendRegent | draw | 402 | 5.077 | 899 | 4.673 | 1637 | 4.008 |
| 27 | act2Start | 17 | 4 | DefendRegent | energyGain | 388 | 9.258 | 892 | 10.138 | 1641 | 10.751 |
| 27 | act2Start | 17 | 4 | DefendRegent | starGain | 402 | 6.542 | 899 | 8.554 | 1627 | 10.213 |
| 27 | act2Start | 17 | 7 | FallingStar | draw | 342 | 9.051 | 643 | 7.562 | 1053 | 5.875 |
| 27 | act2Start | 17 | 7 | FallingStar | energyGain | 310 | 7.788 | 572 | 10.858 | 983 | 13.458 |
| 27 | act2Start | 17 | 7 | FallingStar | starGain | 365 | 6.441 | 654 | 8.465 | 1078 | 9.458 |
| 27 | act2Start | 17 | 10 | Guards | draw | 333 | 13.9 | 378 | 10.329 | 387 | 3.608 |
| 27 | act2Start | 17 | 10 | Guards | energyGain | 345 | 2.211 | 383 | 1.863 | 388 | 3.801 |
| 27 | act2Start | 17 | 10 | Guards | starGain | 327 | 6.327 | 378 | 11.291 | 388 | 13.106 |
| 27 | act2Start | 17 | 12 | RefineBlade+1 | draw | 157 | 7.715 | 314 | 9.961 | 506 | 12.096 |
| 27 | act2Start | 17 | 12 | RefineBlade+1 | energyGain | 352 | 12.718 | 746 | 16.179 | 1296 | 25.821 |
| 27 | act2Start | 17 | 12 | RefineBlade+1 | starGain | 129 | 10.865 | 277 | 12.695 | 456 | 15.498 |
| 27 | act2Start | 17 | 13 | Reflect | draw | 358 | 4.561 | 584 | 3.258 | 899 | 2.835 |
| 27 | act2Start | 17 | 13 | Reflect | energyGain | 363 | 8.666 | 598 | 9.876 | 929 | 11.938 |
| 27 | act2Start | 17 | 13 | Reflect | starGain | 389 | 5.648 | 648 | 8.86 | 981 | 12.281 |
| 27 | act2Start | 17 | 14 | StrikeRegent | draw | 260 | 5.434 | 556 | 3.155 | 905 | 2.818 |
| 27 | act2Start | 17 | 14 | StrikeRegent | energyGain | 347 | 7.007 | 763 | 6.65 | 1371 | 7.46 |
| 27 | act2Start | 17 | 14 | StrikeRegent | starGain | 283 | 5.931 | 585 | 7.984 | 947 | 9.029 |
| 27 | act2Start | 17 | 15 | StrikeRegent | draw | 268 | 5.752 | 559 | 4.804 | 917 | 3.323 |
| 27 | act2Start | 17 | 15 | StrikeRegent | energyGain | 367 | 5.899 | 790 | 6.627 | 1392 | 6.709 |
| 27 | act2Start | 17 | 15 | StrikeRegent | starGain | 304 | 5.689 | 597 | 7.819 | 990 | 9.836 |
| 27 | act2Start | 17 | 16 | StrikeRegent | draw | 227 | 5.473 | 463 | 3.641 | 807 | 2.313 |
| 27 | act2Start | 17 | 16 | StrikeRegent | energyGain | 345 | 6.027 | 731 | 6.772 | 1326 | 7.099 |
| 27 | act2Start | 17 | 16 | StrikeRegent | starGain | 288 | 5.878 | 588 | 7.01 | 984 | 8.091 |
| 27 | act2Start | 17 | 17 | Venerate+1 | draw | 393 | 5.245 | 815 | 4.079 | 1420 | 3.481 |
| 27 | act2Start | 17 | 17 | Venerate+1 | energyGain | 417 | 10.079 | 897 | 12.801 | 1549 | 15.238 |
| 27 | act2Start | 17 | 17 | Venerate+1 | starGain | 410 | 4.402 | 874 | 7.664 | 1530 | 11.188 |
| 28 | final | 47 | 4 | ChildOfTheStars+1 | draw | 321 | 8.746 | 395 | 1.572 | 397 | -1.764 |
| 28 | final | 47 | 4 | ChildOfTheStars+1 | energyGain | 338 | 23.537 | 400 | 20.573 | 400 | 22.683 |
| 28 | final | 47 | 4 | ChildOfTheStars+1 | starGain | 319 | 7.888 | 396 | 8.528 | 399 | 11.755 |
| 28 | final | 47 | 5 | CloakOfStars | draw | 353 | 7.798 | 673 | 4.095 | 973 | 3.93 |
| 28 | final | 47 | 5 | CloakOfStars | energyGain | 376 | 15.952 | 745 | 15.934 | 1129 | 17.271 |
| 28 | final | 47 | 5 | CloakOfStars | starGain | 337 | 4.078 | 694 | 7.572 | 1062 | 10.501 |
| 28 | final | 47 | 6 | Conqueror | draw | 96 | 0.838 | 144 | -1.1 | 203 | -0.154 |
| 28 | final | 47 | 6 | Conqueror | energyGain | 316 | 10.496 | 646 | 10.743 | 1084 | 13.071 |
| 28 | final | 47 | 6 | Conqueror | starGain | 98 | 8.718 | 161 | 10.186 | 240 | 17.832 |
| 28 | final | 47 | 8 | DefendRegent | draw | 248 | 2.511 | 462 | 4.158 | 751 | 4.625 |
| 28 | final | 47 | 8 | DefendRegent | energyGain | 352 | 13.97 | 713 | 14.537 | 1171 | 14.613 |
| 28 | final | 47 | 8 | DefendRegent | starGain | 255 | 1.857 | 497 | 8.371 | 827 | 10.722 |
| 28 | final | 47 | 10 | DefendRegent | draw | 246 | 6.567 | 449 | 1.189 | 693 | -0.083 |
| 28 | final | 47 | 10 | DefendRegent | energyGain | 335 | 14.388 | 704 | 14.773 | 1155 | 14.089 |
| 28 | final | 47 | 10 | DefendRegent | starGain | 251 | 2.983 | 506 | 6.538 | 814 | 9.586 |
| 28 | final | 47 | 12 | FallingStar+1 | draw | 393 | 4.616 | 696 | 2.33 | 958 | 1.615 |
| 28 | final | 47 | 12 | FallingStar+1 | energyGain | 436 | 15.435 | 781 | 16.105 | 1083 | 17.83 |
| 28 | final | 47 | 12 | FallingStar+1 | starGain | 438 | 4.78 | 819 | 7.41 | 1166 | 10.09 |
| 28 | final | 47 | 14 | Glow+1 | draw | 228 | 6.742 | 465 | 5.161 | 746 | 5.882 |
| 28 | final | 47 | 14 | Glow+1 | energyGain | 339 | 15.769 | 722 | 20.852 | 1237 | 24.833 |
| 28 | final | 47 | 14 | Glow+1 | starGain | 217 | 0.571 | 452 | 6.954 | 763 | 13.716 |
| 28 | final | 47 | 16 | Guards+1 | draw | 181 | 37.865 | 275 | 30.022 | 327 | 23.136 |
| 28 | final | 47 | 16 | Guards+1 | energyGain | 193 | 24.841 | 289 | 22.684 | 343 | 18.928 |
| 28 | final | 47 | 16 | Guards+1 | starGain | 169 | 12.028 | 266 | 16.131 | 329 | 17.948 |
| 28 | final | 47 | 17 | GuidingStar+1 | draw | 243 | 2.915 | 413 | -4.274 | 508 | 1.443 |
| 28 | final | 47 | 17 | GuidingStar+1 | energyGain | 287 | 19.264 | 521 | 15.844 | 695 | 16.21 |
| 28 | final | 47 | 17 | GuidingStar+1 | starGain | 255 | 7.133 | 483 | 8.383 | 629 | 10.378 |
| 28 | final | 47 | 29 | Venerate | draw | 171 | 4.021 | 336 | 8.281 | 537 | 6.108 |
| 28 | final | 47 | 29 | Venerate | energyGain | 331 | 7.048 | 690 | 17.05 | 1168 | 21.871 |
| 28 | final | 47 | 29 | Venerate | starGain | 142 | 0.485 | 299 | 3.748 | 509 | 12.443 |
| 29 | final | 47 | 3 | ChildOfTheStars | draw | 266 | 3.114 | 391 | 7.125 | 400 | 0.702 |
| 29 | final | 47 | 3 | ChildOfTheStars | energyGain | 266 | 18.666 | 392 | 22.148 | 400 | 22.255 |
| 29 | final | 47 | 3 | ChildOfTheStars | starGain | 266 | 17.089 | 391 | 22.025 | 400 | 21.55 |
| 29 | final | 47 | 7 | CosmicIndifference+1 | draw | 836 | 7.072 | 2023 | 7.154 | 3463 | 6.05 |
| 29 | final | 47 | 7 | CosmicIndifference+1 | energyGain | 967 | 18.577 | 2767 | 22.756 | 5534 | 25.067 |
| 29 | final | 47 | 7 | CosmicIndifference+1 | starGain | 823 | 10.02 | 2239 | 20.622 | 4133 | 23.46 |
| 29 | final | 47 | 8 | DefendRegent | draw | 236 | 5.788 | 387 | 3.356 | 635 | 5.943 |
| 29 | final | 47 | 8 | DefendRegent | energyGain | 283 | 14.714 | 576 | 20.34 | 983 | 21.697 |
| 29 | final | 47 | 8 | DefendRegent | starGain | 227 | 8.731 | 427 | 20.577 | 727 | 22.693 |
| 29 | final | 47 | 11 | GammaBlast | draw | 229 | 4.655 | 353 | 4.995 | 481 | 4.239 |
| 29 | final | 47 | 11 | GammaBlast | energyGain | 217 | 15.766 | 396 | 18.922 | 582 | 18.91 |
| 29 | final | 47 | 11 | GammaBlast | starGain | 235 | 12.346 | 376 | 19.455 | 542 | 22.771 |
| 29 | final | 47 | 12 | GatherLight | draw | 307 | 8.546 | 603 | 6.09 | 1016 | 3.899 |
| 29 | final | 47 | 12 | GatherLight | energyGain | 337 | 17.355 | 650 | 23.671 | 1065 | 26.013 |
| 29 | final | 47 | 12 | GatherLight | starGain | 311 | 10.021 | 599 | 20.64 | 996 | 24.345 |
| 29 | final | 47 | 17 | Hegemony+1 | draw | 109 | 9.284 | 164 | 30.422 | 213 | 31.79 |
| 29 | final | 47 | 17 | Hegemony+1 | energyGain | 255 | 24.365 | 495 | 38.995 | 809 | 46.03 |
| 29 | final | 47 | 17 | Hegemony+1 | starGain | 123 | 15.405 | 221 | 45.428 | 316 | 59.053 |
| 29 | final | 47 | 19 | PaleBlueDot+1 | draw | 268 | 8.197 | 387 | 11.589 | 400 | 8.285 |
| 29 | final | 47 | 19 | PaleBlueDot+1 | energyGain | 268 | 17.766 | 387 | 22.357 | 400 | 19.761 |
| 29 | final | 47 | 19 | PaleBlueDot+1 | starGain | 268 | 12.299 | 387 | 19.693 | 400 | 20.874 |
| 29 | final | 47 | 21 | PillarOfCreation+1 | draw | 286 | 7.302 | 393 | 8.776 | 400 | 3.56 |
| 29 | final | 47 | 21 | PillarOfCreation+1 | energyGain | 286 | 17.358 | 393 | 19.202 | 400 | 18.653 |
| 29 | final | 47 | 21 | PillarOfCreation+1 | starGain | 286 | 13.269 | 393 | 18.39 | 400 | 18.665 |
| 29 | final | 47 | 22 | Quasar+1 | draw | 214 | 1.862 | 338 | 4.488 | 503 | 4.949 |
| 29 | final | 47 | 22 | Quasar+1 | energyGain | 224 | 20.945 | 362 | 23.491 | 568 | 28.826 |
| 29 | final | 47 | 22 | Quasar+1 | starGain | 218 | 12.27 | 347 | 16.768 | 544 | 23.947 |
| 29 | final | 47 | 24 | StrikeRegent | draw | 170 | 1.584 | 273 | 7.799 | 378 | 8.124 |
| 29 | final | 47 | 24 | StrikeRegent | energyGain | 266 | 7.962 | 516 | 10.356 | 931 | 11.707 |
| 29 | final | 47 | 24 | StrikeRegent | starGain | 108 | 13.781 | 194 | 24.775 | 318 | 22.132 |
| 30 | final | 47 | 0 | Alignment+1 | draw | 331 | 20.758 | 593 | 28.656 | 1027 | 35.078 |
| 30 | final | 47 | 0 | Alignment+1 | energyGain | 265 | 7.372 | 481 | 11.232 | 838 | 14.489 |
| 30 | final | 47 | 0 | Alignment+1 | starGain | 289 | 7.06 | 525 | 14.506 | 914 | 12.643 |
| 30 | final | 47 | 1 | Arsenal+1 | draw | 400 | 11.339 | 400 | 15.294 | 400 | 20.915 |
| 30 | final | 47 | 1 | Arsenal+1 | energyGain | 400 | 11.046 | 400 | 13.022 | 400 | 16.103 |
| 30 | final | 47 | 1 | Arsenal+1 | starGain | 400 | 10.154 | 400 | 13.679 | 400 | 10.844 |
| 30 | final | 47 | 3 | Begone+1 | draw | 270 | 11.905 | 590 | 13.188 | 1124 | 12.39 |
| 30 | final | 47 | 3 | Begone+1 | energyGain | 351 | 15.758 | 856 | 22.023 | 1845 | 31.283 |
| 30 | final | 47 | 3 | Begone+1 | starGain | 252 | 9.473 | 601 | 11.778 | 1196 | 16.961 |
| 30 | final | 47 | 5 | CloakOfStars | draw | 378 | 7.63 | 844 | 13.659 | 1586 | 15.994 |
| 30 | final | 47 | 5 | CloakOfStars | energyGain | 372 | 15.424 | 839 | 19.645 | 1592 | 26.37 |
| 30 | final | 47 | 5 | CloakOfStars | starGain | 407 | 5.893 | 905 | 12.915 | 1695 | 16.073 |
| 30 | final | 47 | 7 | CosmicIndifference | draw | 581 | 6.368 | 1215 | 2.991 | 2080 | 1.311 |
| 30 | final | 47 | 7 | CosmicIndifference | energyGain | 981 | 11.007 | 2376 | 12.952 | 4106 | 18.634 |
| 30 | final | 47 | 7 | CosmicIndifference | starGain | 483 | 7.14 | 1017 | 10.773 | 1662 | 12.215 |
| 30 | final | 47 | 8 | DefendRegent | draw | 289 | 9.215 | 637 | 7.068 | 1198 | 3.238 |
| 30 | final | 47 | 8 | DefendRegent | energyGain | 356 | 11.522 | 811 | 10.829 | 1583 | 13.583 |
| 30 | final | 47 | 8 | DefendRegent | starGain | 256 | 12.22 | 576 | 10.749 | 1078 | 9.196 |
| 30 | final | 47 | 15 | Guards | draw | 291 | 23.046 | 365 | 26.24 | 385 | 40.087 |
| 30 | final | 47 | 15 | Guards | energyGain | 283 | 8.663 | 373 | 16.023 | 389 | 27.414 |
| 30 | final | 47 | 15 | Guards | starGain | 259 | 10.347 | 349 | 12.564 | 382 | 11.659 |
| 30 | final | 47 | 16 | KinglyPunch+1 | draw | 226 | 12.926 | 515 | 11.964 | 968 | 5.72 |
| 30 | final | 47 | 16 | KinglyPunch+1 | energyGain | 362 | 10.692 | 812 | 9.897 | 1587 | 9.918 |
| 30 | final | 47 | 16 | KinglyPunch+1 | starGain | 198 | 7.663 | 480 | 9.291 | 848 | 10.274 |
| 30 | final | 47 | 20 | SpoilsOfBattle+1 | draw | 279 | 8.846 | 592 | 17.448 | 1146 | 23.398 |
| 30 | final | 47 | 20 | SpoilsOfBattle+1 | energyGain | 385 | 17.835 | 914 | 27.023 | 1789 | 40.707 |
| 30 | final | 47 | 20 | SpoilsOfBattle+1 | starGain | 274 | 10.99 | 641 | 20.529 | 1238 | 28.364 |
| 30 | final | 47 | 21 | Venerate+1 | draw | 308 | 12.57 | 744 | 18.811 | 1506 | 27.123 |
| 30 | final | 47 | 21 | Venerate+1 | energyGain | 354 | 15.841 | 850 | 20.976 | 1666 | 27.388 |
| 30 | final | 47 | 21 | Venerate+1 | starGain | 265 | 5.497 | 644 | 11.401 | 1219 | 13.221 |
| 31 | final | 47 | 1 | BigBang | draw | 299 | 8.503 | 389 | 6.865 | 389 | -1.074 |
| 31 | final | 47 | 1 | BigBang | energyGain | 305 | 11.309 | 391 | 23.795 | 391 | 52.339 |
| 31 | final | 47 | 1 | BigBang | starGain | 306 | 1.42 | 392 | 2.136 | 392 | 4.845 |
| 31 | final | 47 | 2 | BulkUp+1 | draw | 294 | 8.603 | 396 | 7.945 | 397 | 11.969 |
| 31 | final | 47 | 2 | BulkUp+1 | energyGain | 300 | 25.36 | 398 | 33.193 | 398 | 42.481 |
| 31 | final | 47 | 2 | BulkUp+1 | starGain | 299 | 0.908 | 398 | 3.528 | 398 | 12.232 |
| 31 | final | 47 | 6 | DefendRegent | draw | 237 | 6.911 | 598 | 9.397 | 1220 | 8.861 |
| 31 | final | 47 | 6 | DefendRegent | energyGain | 294 | 10.288 | 655 | 15.412 | 1331 | 19.633 |
| 31 | final | 47 | 6 | DefendRegent | starGain | 243 | -1.844 | 599 | 0.407 | 1226 | 8.137 |
| 31 | final | 47 | 11 | FallingStar | draw | 318 | 14.603 | 795 | 15.007 | 1364 | 15.289 |
| 31 | final | 47 | 11 | FallingStar | energyGain | 311 | 11.399 | 740 | 16.855 | 1358 | 20.746 |
| 31 | final | 47 | 11 | FallingStar | starGain | 319 | 1.067 | 785 | 2.886 | 1476 | 5.551 |
| 31 | final | 47 | 15 | Havoc+1 | draw | 140 | 9.711 | 184 | 13.128 | 186 | 21.972 |
| 31 | final | 47 | 15 | Havoc+1 | energyGain | 134 | 15.328 | 175 | 27.55 | 179 | 58.308 |
| 31 | final | 47 | 15 | Havoc+1 | starGain | 131 | 2.586 | 168 | 4.91 | 172 | 11.693 |
| 31 | final | 47 | 18 | KinglyKick | draw | 25 | 19.104 | 140 | 19.514 | 403 | 21.183 |
| 31 | final | 47 | 18 | KinglyKick | energyGain | 27 | 5.185 | 142 | 6.485 | 399 | 4.638 |
| 31 | final | 47 | 18 | KinglyKick | starGain | 24 | 1.767 | 141 | 0.196 | 407 | 2.126 |
| 31 | final | 47 | 19 | Patter+1 | draw | 333 | 7.653 | 839 | 10.272 | 1954 | 7.159 |
| 31 | final | 47 | 19 | Patter+1 | energyGain | 358 | 13.072 | 825 | 18.741 | 1763 | 22.304 |
| 31 | final | 47 | 19 | Patter+1 | starGain | 326 | 2.499 | 794 | 2.109 | 1778 | 6.155 |
| 31 | final | 47 | 20 | Quasar+1 | draw | 209 | 7.263 | 309 | 15.196 | 321 | 19.931 |
| 31 | final | 47 | 20 | Quasar+1 | energyGain | 218 | 17.903 | 331 | 32.676 | 353 | 54.681 |
| 31 | final | 47 | 20 | Quasar+1 | starGain | 211 | 3.625 | 310 | 3.009 | 332 | 1.204 |
| 31 | final | 47 | 26 | StrikeRegent | draw | 127 | 9.805 | 319 | 10.77 | 493 | 10.987 |
| 31 | final | 47 | 26 | StrikeRegent | energyGain | 258 | 7.122 | 577 | 11.368 | 959 | 13.197 |
| 31 | final | 47 | 26 | StrikeRegent | starGain | 124 | 1.4 | 309 | -0.674 | 472 | 2.245 |
| 31 | final | 47 | 29 | VoidForm | draw | 94 | 2.111 | 119 | 9.291 | 119 | 14.941 |
| 31 | final | 47 | 29 | VoidForm | energyGain | 95 | -0.349 | 121 | 0.79 | 121 | 9.607 |
| 31 | final | 47 | 29 | VoidForm | starGain | 98 | -0.988 | 123 | 6.27 | 123 | 26.234 |
