namespace Trellis.Core

open System

/// Global, network-wide parameters and cutover conditions
module ChainParams =

    /// Cutover configuration for transitioning from PoW->PoS
    type CutoverConfig = {
        /// Minimum block height required to activate cutover
        MinHeight: uint64
        /// Minimum MedianTimePast required to activate cutover
        MinMedianTime: DateTime
        /// Optional Terminal Total Difficulty (TTD)
        MinTotalDifficulty: uint64 option
    }

    /// Default parameters (can be made network-specific)
    let defaultCutover : CutoverConfig =
        let cfg = TrellisConfig.current
        let genesis = DateTime.UtcNow
        {
            MinHeight = cfg.CutoverMinHeight
            MinMedianTime = genesis.AddDays(cfg.CutoverMinMedianTimeDays)
            MinTotalDifficulty = cfg.CutoverMinTotalDifficulty
        }

    /// Network chain ID (domain separation for signatures)
    let networkChainId : uint32 = TrellisConfig.current.ChainId

    /// Evaluate cutover condition using composite guard (height AND time AND optional TTD)
    let isCutoverSatisfied (cfg: CutoverConfig) (currentHeight: uint64) (medianTimePast: DateTime) (totalDifficulty: uint64 option) : bool =
        let heightOk = currentHeight >= cfg.MinHeight
        let timeOk = medianTimePast >= cfg.MinMedianTime
        let ttdOk =
            match cfg.MinTotalDifficulty, totalDifficulty with
            | None, _ -> true
            | Some ttd, Some td -> td >= ttd
            | Some _, None -> false
        heightOk && timeOk && ttdOk

    /// DTO for reporting current cutover status
    type CutoverStatus = {
        CurrentHeight: uint64
        MedianTimePast: DateTime
        TotalDifficulty: uint64
        MinHeight: uint64
        MinMedianTime: DateTime
        MinTotalDifficulty: uint64 option
        IsActive: bool
    }
