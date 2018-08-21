# PanicSystem
Forked from RealityMachina's fork (https://github.com/RealityMachina/Basic-Panic-System) of mpstark's PunchinOut mod (https://github.com/Mpstark/PunchinOut).

Design improvements by don Zappo, balancing and testing by ganimal, and coding by gnivler.

Huge thanks to JetBrains for supporting this project and OSS development.  
<a href="https://jetbrains.com"><img src="jetbrains-variant-4.png" width="10%" height="10%"></a><a href="https://www.jetbrains.com/rider"><img src="logo.png" width="5%" height="5%"></a>
Coded using JetBrains Rider IDE.

## Installation

Install like any ModTek mod.

## General Details

There are four states for a pilot to be in: Confident, Unsettled, Stressed, and Panicked.

Panic rolls are made when sufficient damage is dealt.  This roll considers multiple factors to calculate a saving throw and a failure will increase panic by one level.  Guts and Tactics can affect this, as well as Quirks from don Zappo's [Pilot Quirks mod](https://www.nexusmods.com/battletech/mods/282/).  Lance morale also comes into play.  Rolling 100 on a saving throw will reduce panic level by one state.  Succeeding all saving throws in a turn will improve panic state by one level as well.

Panic save failures lead to ejection rolls when the target is at Panicked state.  Similar calculations are performed to determine a saving throw, where a failure will eject the pilot.

The panic states affect the pilot's to-hit and to-hit-against stats as follows.

## Panic Effects (defaults)

Panic State|To Hit|To Hit Against
-----------|------|--------------
Confident|0|0
Unsettled|+1|0
Stressed| +1|-1
Panicked| +2|-2

## Special Cases

Pilots with one health left and a mostly destroyed mech will automatically starting rolling for ejection, skipping panic saving throws.  Pilots who are alone and face insurmountable odds will also skip panic throws and start rolling for ejection.
