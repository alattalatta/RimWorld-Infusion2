module Infusion.VerseInterop

open RimWorld
open Verse

open Lib

let apparelsOfPawn (pawn: Pawn): option<list<Apparel>> =
    Option.ofObj pawn.apparel |> Option.map (fun tracker -> List.ofSeq tracker.WornApparel)

let equipmentsOfPawn (pawn: Pawn): option<list<ThingWithComps>> =
    Option.ofObj pawn.equipment |> Option.map (fun tracker -> List.ofSeq tracker.AllEquipmentListForReading)

let compOfThing<'C when 'C :> ThingComp and 'C: null> (thing: Thing) = Option.ofObj (thing.TryGetComp<'C>())

let stuffOfThing (thing: Thing) = Option.ofObj thing.Stuff

let translate (key: string) = key.TranslateSimple()

module DamageInfo =
    let setAngle angle (di: DamageInfo) =
        di.SetAngle angle
        di

    let setBodyRegion height depth (di: DamageInfo) =
        di.SetBodyRegion(height, depth)
        di

    let setWeaponBodyPartGroup bodyPartGroup (di: DamageInfo) =
        di.SetWeaponBodyPartGroup(bodyPartGroup)
        di