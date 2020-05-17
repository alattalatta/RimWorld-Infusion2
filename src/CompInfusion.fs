namespace Infusion

open System
open System.Collections.Generic
open System.Text

open RimWorld
open Verse

open DefFields
open VerseTools
open Lib
open StatMod
open VerseInterop

type BestInfusionLabelLength =
    | Long
    | Short


// Holds an equipment's infusions.
[<AllowNullLiteral>]
type CompInfusion() =
    inherit ThingComp()

    static let mutable wantingCandidates = Set.empty<CompInfusion>
    static let mutable extractionCandidates = Set.empty<CompInfusion>
    static let mutable removalCandidates = Set.empty<CompInfusion>

    let mutable infusions = Set.empty<InfusionDef>
    let mutable wantingSet = Set.empty<InfusionDef>
    let mutable extractionSet = Set.empty<InfusionDef>
    let mutable removalSet = Set.empty<InfusionDef>

    let mutable slotCount = -1
    let mutable quality = QualityCategory.Normal

    let mutable bestInfusionCache = None
    let mutable extraDamageCache = None
    let mutable extraExplosionCache = None

    let infusionsStatModCache = Dictionary<StatDef, option<StatMod>>()

    static member WantingCandidates
        with get () = wantingCandidates
        and set value = do wantingCandidates <- value

    static member ExtractionCandidates
        with get () = extractionCandidates
        and set value = do extractionCandidates <- value

    static member RemovalCandidates
        with get () = removalCandidates
        and set value = do removalCandidates <- value

    static member RegisterWantingCandidate comp =
        do CompInfusion.WantingCandidates <- Set.add comp CompInfusion.WantingCandidates

    static member RegisterExtractionCandidate comp =
        do CompInfusion.ExtractionCandidates <- Set.add comp CompInfusion.ExtractionCandidates

    static member RegisterRemovalCandidate comp =
        do CompInfusion.RemovalCandidates <- Set.add comp CompInfusion.RemovalCandidates

    static member UnregisterWantingCandidates comp =
        do CompInfusion.WantingCandidates <- Set.remove comp CompInfusion.WantingCandidates

    static member UnregisterExtractionCandidates comp =
        do CompInfusion.ExtractionCandidates <- Set.remove comp CompInfusion.ExtractionCandidates

    static member UnregisterRemovalCandidate comp =
        do CompInfusion.RemovalCandidates <- Set.remove comp CompInfusion.RemovalCandidates

    member this.Quality
        with get () = quality
        and set value =
            do quality <- value
            do slotCount <- this.CalculateSlotCount()

    member this.Infusions
        with get () = infusions |> Seq.sortDescending
        and set (value: seq<InfusionDef>) =
            do this.InvalidateCache()
            do infusions <- value |> Set.ofSeq
            do wantingSet <- Set.difference wantingSet infusions
            do extractionSet <- Set.intersect extractionSet infusions
            do removalSet <- Set.intersect removalSet infusions

            this.FinalizeSetMutations()

    member this.InfusionsRaw = infusions

    member this.WantingSet
        with get () = wantingSet
        and set value =
            do wantingSet <- value

            this.FinalizeSetMutations()

    member this.FirstWanting = Seq.tryHead wantingSet

    member this.ExtractionSet
        with get () = extractionSet
        and set value =
            do extractionSet <- value
            do removalSet <- Set.difference removalSet extractionSet

            this.FinalizeSetMutations()

    member this.FirstExtraction = Seq.tryHead extractionSet

    member this.RemovalSet
        with get () = removalSet
        and set value =
            do removalSet <- value
            do extractionSet <- Set.difference extractionSet removalSet

            this.FinalizeSetMutations()

    member this.InfusionsByPosition =
        let (prefixes, suffixes) =
            this.Infusions
            |> Seq.fold (fun (pre, suf) cur ->
                if cur.position = Position.Prefix then (cur :: pre, suf) else (pre, cur :: suf))
                   (List.empty, List.empty)

        (List.rev prefixes, List.rev suffixes)

    member this.BestInfusion =
        match bestInfusionCache with
        | None ->
            do bestInfusionCache <- this.Infusions |> Seq.tryHead
            bestInfusionCache
        | _ -> bestInfusionCache

    member this.ExtraDamages =
        if Option.isNone extraDamageCache
        then do extraDamageCache <- InfusionDef.collectExtraEffects (fun def -> def.ExtraDamages) infusions

        Option.defaultValue Seq.empty extraDamageCache

    member this.ExtraExplosions =
        if Option.isNone extraExplosionCache
        then do extraExplosionCache <- InfusionDef.collectExtraEffects (fun def -> def.ExtraExplosions) infusions

        Option.defaultValue Seq.empty extraExplosionCache

    member this.SlotCount = slotCount

    member this.Descriptions =
        this.Infusions
        |> Seq.map (fun inf -> inf.GetDescriptionString())
        |> String.concat "\n\n"

    // [todo] move to under ITab
    member this.InspectionLabel =
        if Set.isEmpty infusions then
            (translate1 "Infusion.Label.NotInfused" this.parent.def.label).CapitalizeFirst()
            |> string
        else
            let (prefixes, suffixes) = this.InfusionsByPosition

            let suffixedPart =
                if List.isEmpty suffixes then
                    this.parent.def.label
                else
                    let suffixString =
                        (suffixes |> List.map (fun def -> def.label)).ToCommaList(true)

                    string (translate2 "Infusion.Label.Suffixed" suffixString this.parent.def.label)

            let prefixedPart =
                if List.isEmpty prefixes then
                    suffixedPart
                else
                    let prefixString =
                        prefixes
                        |> List.map (fun def -> def.label)
                        |> String.concat " "

                    string (translate2 "Infusion.Label.Prefixed" prefixString suffixedPart)

            prefixedPart.CapitalizeFirst()

    member this.Size = Set.count infusions

    member this.PopulateInfusionsStatModCache(stat: StatDef) =
        if not (infusionsStatModCache.ContainsKey stat) then
            let elligibles =
                infusions
                |> Seq.filter (fun inf -> inf.stats.ContainsKey stat)
                |> Seq.map (fun inf -> inf.stats.TryGetValue stat)

            let statMod =
                if Seq.isEmpty elligibles
                then None
                else elligibles |> Seq.fold (+) StatMod.empty |> Some

            do infusionsStatModCache.Add(stat, statMod)

    member this.CalculateSlotCount() =
        let apparelProps = Option.ofObj this.parent.def.apparel

        // limit by body part group count
        let limit =
            if Settings.SlotModifiers.bodyPartHandle.Value then
                apparelProps
                |> Option.map (fun a -> a.bodyPartGroups.Count)
                |> Option.defaultValue Int32.MaxValue
            elif quality < QualityCategory.Normal then
                0
            else
                Int32.MaxValue

        let layerBonus =
            apparelProps
            |> Option.map (fun a -> if Settings.SlotModifiers.layerHandle.Value then a.layers.Count - 1 else 0)
            |> Option.defaultValue 0

        min limit (Settings.Slots.getBaseSlotsFor this.Quality)
        + layerBonus

    member this.GetModForStat(stat: StatDef) =
        do this.PopulateInfusionsStatModCache(stat)
        infusionsStatModCache.TryGetValue(stat, None)
        |> Option.defaultValue StatMod.empty

    member this.HasInfusionForStat(stat: StatDef) =
        do this.PopulateInfusionsStatModCache(stat)
        infusionsStatModCache.TryGetValue(stat, None)
        |> Option.isSome

    member this.InvalidateCache() =
        do bestInfusionCache <- None
        do infusionsStatModCache.Clear()

    member this.MarkForInfuser(infDef: InfusionDef) = do this.WantingSet <- Set.add infDef wantingSet
    member this.MarkForExtractor(infDef: InfusionDef) = do this.ExtractionSet <- Set.add infDef extractionSet
    member this.MarkForRemoval(infDef: InfusionDef) = do this.RemovalSet <- Set.add infDef removalSet

    member this.UnmarkForInfuser(infDef: InfusionDef) = do this.WantingSet <- Set.remove infDef wantingSet
    member this.UnmarkForExtractor(infDef: InfusionDef) = do this.ExtractionSet <- Set.remove infDef extractionSet
    member this.UnmarkForRemoval(infDef: InfusionDef) = do this.RemovalSet <- Set.remove infDef removalSet

    member this.FinalizeSetMutations() =
        if Set.isEmpty wantingSet
        then CompInfusion.UnregisterWantingCandidates this
        else CompInfusion.RegisterWantingCandidate this

        if Set.isEmpty extractionSet
        then CompInfusion.UnregisterExtractionCandidates this
        else CompInfusion.RegisterExtractionCandidate this

        if Set.isEmpty removalSet
        then CompInfusion.UnregisterRemovalCandidate this
        else CompInfusion.RegisterRemovalCandidate this

    member this.MakeBestInfusionLabel length =
        match this.BestInfusion with
        | Some bestInf ->
            let sb =
                StringBuilder(if length = Long then bestInf.label else bestInf.LabelShort)

            if this.Size > 1 then
                do sb.Append("(+").Append(this.Size - 1).Append(")")
                   |> ignore

            string sb
        | None -> ""

    override this.TransformLabel label =
        match this.BestInfusion with
        | Some bestInf ->
            let parent = this.parent

            let baseLabel =
                GenLabel.ThingLabel(parent.def, parent.Stuff)

            let sb =
                match bestInf.position with
                | Position.Prefix -> translate2 "Infusion.Label.Prefixed" (this.MakeBestInfusionLabel Long) baseLabel
                | Position.Suffix -> translate2 "Infusion.Label.Suffixed" (this.MakeBestInfusionLabel Long) baseLabel
                | _ -> raise (ArgumentException("Position must be either Prefix or Suffix"))
                |> string
                |> StringBuilder

            // components
            // quality should never be None but let's be cautious
            let quality =
                compOfThing<CompQuality> parent
                |> Option.map (fun cq -> cq.Quality.GetLabel())

            let hitPoints =
                if parent.def.useHitPoints
                   && parent.HitPoints < parent.MaxHitPoints
                   && parent.def.stackLimit = 1 then
                    Some
                        ((float32 parent.HitPoints
                          / float32 parent.MaxHitPoints).ToStringPercent())
                else
                    None

            let tainted =
                match parent with
                | :? Apparel as apparel -> if apparel.WornByCorpse then Some(translate "WornByCorpseChar") else None
                | _ -> None

            do [ quality; hitPoints; tainted ]
               |> List.choose id
               |> String.concat " "
               |> (fun str ->
                   if not (str.NullOrEmpty())
                   then sb.Append(" (").Append(str).Append(")") |> ignore)

            string sb
        | None -> label

    override this.PostSpawnSetup(respawningAfterLoad) =
        if not respawningAfterLoad
           && slotCount = -1
           && quality >= QualityCategory.Normal then
            do slotCount <- this.CalculateSlotCount()

        if not (respawningAfterLoad || Seq.isEmpty removalSet)
        then do CompInfusion.RegisterRemovalCandidate this

    override this.PostDeSpawn(_) = do CompInfusion.UnregisterRemovalCandidate this

    override this.GetDescriptionPart() = this.Descriptions

    override this.DrawGUIOverlay() =
        if Find.CameraDriver.CurrentZoom
           <= CameraZoomRange.Close then
            match this.BestInfusion with
            | Some bestInf ->
                do GenMapUI.DrawThingLabel
                    (GenMapUI.LabelDrawPosFor(this.parent, -0.6499999762f),
                     this.MakeBestInfusionLabel Short,
                     bestInf.tier.color)
            | None -> ()

    override this.PostExposeData() =
        scribeValue "quality" this.Quality
        |> Option.iter (fun qc -> do this.Quality <- qc)

        scribeValueWithDefault "slotCount" (this.CalculateSlotCount()) this.SlotCount
        |> Option.iter (fun sc -> do slotCount <- sc)

        scribeDefCollection "infusion" infusions
        |> Option.iter (fun infs ->
            do this.Infusions <-
                infs
                |> Seq.filter (InfusionDef.gracefullyDie >> not)
                |> Seq.map (fun inf ->
                    inf.Migration
                    |> Option.bind (fun m -> m.Replace)
                    |> Option.defaultValue inf))

        scribeDefCollection "wanting" wantingSet
        |> Option.map Set.ofSeq
        |> Option.iter (fun infs -> do wantingSet <- infs)

        scribeDefCollection "removal" removalSet
        |> Option.map Set.ofSeq
        |> Option.iter (fun infs ->
            do removalSet <-
                infs
                |> Set.filter (InfusionDef.gracefullyDie >> not))

    override this.AllowStackWith(other) =
        compOfThing<CompInfusion> other
        |> Option.map (fun comp -> infusions = comp.InfusionsRaw)
        |> Option.defaultValue false

    override this.GetHashCode() = this.parent.thingIDNumber

    override this.Equals(ob) =
        match ob with
        | :? CompInfusion as comp -> this.parent.thingIDNumber = comp.parent.thingIDNumber
        | _ -> false

    interface IComparable with
        member this.CompareTo(ob) =
            match ob with
            | :? CompInfusion as comp ->
                let thingID = comp.parent.ThingID
                this.parent.ThingID.CompareTo thingID
            | _ -> 0


module CompInfusion =
    let addInfusion infDef (comp: CompInfusion) =
        do comp.Infusions <-
            seq {
                yield infDef
                yield! comp.Infusions
            }

    let setInfusions infDefs (comp: CompInfusion) = do comp.Infusions <- infDefs

    let compatibleWith (comp: CompInfusion) (infDef: InfusionDef) =
        let parent = comp.parent

        // requirement fields
        let checkAllowance (infDef: InfusionDef) =
            if parent.def.IsApparel then infDef.requirements.allowance.apparel
            elif parent.def.IsMeleeWeapon then infDef.requirements.allowance.melee
            elif parent.def.IsRangedWeapon then infDef.requirements.allowance.ranged
            else false

        let checkTechLevel (infDef: InfusionDef) =
            infDef.requirements.techLevel
            |> Seq.contains parent.def.techLevel

        let checkQuality (infDef: InfusionDef) = (infDef.ChanceFor comp.Quality) > 0.0f

        // 'complex' requirements
        // is the projectile of parent _the_ Bullet?
        // needed, as I can't patch all the possible bullet classes
        // [todo] make it XML-able, i.e. <li Class="Infusion.Complex.RequireBullet" />
        let checkBulletClass (infDef: InfusionDef) =
            if not parent.def.IsRangedWeapon
               || not (infDef.requirements.needBulletClass) then
                true
            else
                Seq.tryHead parent.def.Verbs
                |> Option.bind (fun a -> Option.ofObj a.defaultProjectile)
                |> Option.map (fun a -> a.thingClass = typeof<Bullet>)
                |> Option.defaultValue false

        // is the parent ShieldBelt?
        let checkShieldBelt (infDef: InfusionDef) =
            if not infDef.requirements.allowance.apparel
               || not infDef.requirements.shieldBelt then
                true
            else
                parent.def.thingClass = typeof<ShieldBelt>

        // is the damage Sharp/Blunt?
        let checkDamageType (infDef: InfusionDef) =
            if parent.def.IsApparel
               || infDef.requirements.meleeDamageType = DamageType.Anything then
                true
            else
                parent.def.tools
                |> Seq.reduce (fun a b -> if a.power > b.power then a else b)
                |> isToolCapableOfDamageType infDef.requirements.meleeDamageType

        (checkAllowance
         <&> checkTechLevel
         <&> checkQuality
         <&> checkBulletClass
         <&> checkShieldBelt
         <&> checkDamageType) infDef

    /// Picks elligible `InfusionDef` for the `Thing`.
    let pickInfusions quality (comp: CompInfusion) =
        // chance
        let checkChance (infDef: InfusionDef) =
            let chance =
                infDef.ChanceFor(quality)
                * Settings.SelectionConsts.chanceHandle.Value

            Rand.Chance chance

        DefDatabase<InfusionDef>.AllDefs
        |> Seq.filter
            ((InfusionDef.disabled >> not)
             <&> compatibleWith comp)
        // (infusionDef * weight)
        |> Seq.map (fun infDef ->
            (infDef,
             (infDef.WeightFor quality)
             * Settings.SelectionConsts.weightHandle.Value
             + Rand.Value)) // weighted, duh
        |> Seq.sortByDescending snd
        |> Seq.truncate comp.SlotCount
        |> Seq.map fst
        |> Seq.filter checkChance
        |> List.ofSeq // need to "finalize" the random sort
        |> List.sort

    let removeMarkedInfusions (comp: CompInfusion) =
        let hitPointsRatio =
            float32 comp.parent.HitPoints
            / float32 comp.parent.MaxHitPoints

        do comp.Infusions <- Set.difference comp.InfusionsRaw comp.RemovalSet
        do comp.RemovalSet <- Set.empty // maybe not needed

        do comp.parent.HitPoints <- int (round (float32 comp.parent.MaxHitPoints * hitPointsRatio))

    let removeAllInfusions (comp: CompInfusion) = do comp.Infusions <- Set.empty

    let removeInfusion def (comp: CompInfusion) =
        do comp.Infusions <- Set.remove def comp.InfusionsRaw