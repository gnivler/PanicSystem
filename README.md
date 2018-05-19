# Basic Panic System
ModTek mod that adds a basic panic system for MechWarriors, both playable and non-playable into the game. Forked from mpstark's PunchinOut mod (https://github.com/Mpstark/PunchinOut).

## Installation

Install [BTML](https://github.com/Mpstark/BattleTechModLoader) and [ModTek](https://github.com/Mpstark/ModTek). Extract files to `BATTLETECH\Mods\BasicPanicSystem\`.

## Basic Details

There are four states for a pilot to be in: Normal, Fatigued, Stressed, and Panicked.

Every time a pilot takes an attack in a mech, they roll for panic. This roll is increased in strength by how powerful the attack was, where it hit, how damanged the pilot's mech is, is the rest of their lance dead or gone, etc. It is decreased by a pilot's tactics and guts scores.

If this roll succeeds, they are then knocked down to the next lower state. Once they hit Panicked, they then start rolling for ejection chances.

By default, pilots can only change panic states once per turn, to prevent runaway panic attacks from alpha strikes.

## Special Cases

Pilot injuries are a special exception: any time a pilot gets hurt, they get knocked down to the next state automatically. 

## Configuration

`mod.json` has some settings on how the chances work -- for a simple change to the max ejection roll, just change the `MaxEjectChance`. Rolls for the general panic system should be relatively self-explanatory.
