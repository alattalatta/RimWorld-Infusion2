module Infusion.DefFields

open RimWorld

type Allowance =
    val mutable apparel: bool
    val mutable melee: bool
    val mutable ranged: bool

    new() =
        { apparel = false
          melee = false
          ranged = false }

type QualityMap =
    val mutable awful: float32
    val mutable poor: float32
    val mutable normal: float32
    val mutable good: float32
    val mutable excellent: float32
    val mutable masterwork: float32
    val mutable legendary: float32

    new() =
        { awful = 0.0f
          poor = 0.0f
          normal = 0.0f
          good = 0.0f
          excellent = 0.0f
          masterwork = 0.0f
          legendary = 0.0f }


type Position =
    | Prefix = 0
    | Suffix = 1


type DamageType =
    | Anything = 0
    | Blunt = 1
    | Sharp = 2


type Requirements =
    val mutable allowance: Allowance
    val mutable techLevel: ResizeArray<TechLevel>
    val mutable meleeDamageType: DamageType

    new() =
        { allowance = Allowance()
          techLevel = ResizeArray()
          meleeDamageType = DamageType.Anything }


type Tier =
    | Awful = 0
    | Poor = 1
    | Common = 2
    | Uncommon = 3
    | Rare = 4
    | Epic = 5
    | Legendary = 6
    | Artifact = 7