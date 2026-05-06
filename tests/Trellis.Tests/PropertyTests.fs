namespace Trellis.Tests

open Trellis.Core
open FsCheck
open System

/// Property-based tests for Trellis blockchain
module PropertyTests =

    /// Cryptographic properties
    module CryptoProperties =

        /// Hash function should be deterministic
        let hashDeterministic (data: byte[]) =
            let hash1 = SimpleCrypto.sha256 data
            let hash2 = SimpleCrypto.sha256 data
            hash1 = hash2

        /// Hash function should produce different outputs for different inputs
        let hashDifferent (data1: byte[]) (data2: byte[]) =
            if data1 <> data2 then
                let hash1 = SimpleCrypto.sha256 data1
                let hash2 = SimpleCrypto.sha256 data2
                hash1 <> hash2
            else
                true

        /// Hash should always produce 32-byte output
        let hashLength (data: byte[]) =
            let hash = SimpleCrypto.sha256 data
            let (SimpleTypes.Hash hashBytes) = hash
            hashBytes.Length = 32

        /// Signature verification should work for valid signatures
        let signatureVerification (data: byte[]) =
            let (publicKey, privateKey) = SimpleCrypto.generateKeyPair()
            let signature = SimpleCrypto.sign privateKey data
            SimpleCrypto.verifySignature publicKey data signature

        /// Signature verification should fail for invalid signatures
        let signatureInvalid (data: byte[]) (wrongData: byte[]) =
            if data <> wrongData then
                let (publicKey, privateKey) = SimpleCrypto.generateKeyPair()
                let signature = SimpleCrypto.sign privateKey data
                not (SimpleCrypto.verifySignature publicKey wrongData signature)
            else
                true

    /// Staking properties
    module StakingProperties =

        /// Block rewards should decrease with halving
        let blockRewardHalving (height1: SimpleTypes.BlockHeight) (height2: SimpleTypes.BlockHeight) =
            let (SimpleTypes.BlockHeight h1) = height1
            let (SimpleTypes.BlockHeight h2) = height2
            if h2 > h1 then
                let reward1 = SimpleCrypto.Staking.calculateBlockReward height1
                let reward2 = SimpleCrypto.Staking.calculateBlockReward height2
                let (SimpleTypes.Balance r1) = reward1
                let (SimpleTypes.Balance r2) = reward2
                r2 <= r1
            else
                true

        /// Fee burning should be proportional to fee amount
        let feeBurningProportional (totalFee: SimpleTypes.Balance) =
            let (SimpleTypes.Balance feeAmount) = totalFee
            if feeAmount > 0UL then
                let expectedBurn = uint64 (decimal feeAmount * TrellisConfig.current.BurnRate)
                let feeBurning = SimpleCrypto.Staking.processFeeBurning (SimpleTypes.Hash(Array.zeroCreate 32)) totalFee
                match feeBurning with
                | Some burn ->
                    let (SimpleTypes.Balance burnedAmount) = burn.BurnedAmount
                    burnedAmount = expectedBurn && burnedAmount > 0UL && burnedAmount <= feeAmount
                | None -> expectedBurn = 0UL
            else
                true

    let private runPropertyTest (name: string) (propertyTest: 'a) =
        try
            Check.QuickThrowOnFailure propertyTest
            printfn "PASS: %s" name
            true
        with
        | ex ->
            printfn "FAIL: %s - %s" name ex.Message
            false

    /// Run all property tests
    let runPropertyTests () =
        printfn "Running Property-Based Tests..."
        printfn "================================="

        printfn "Testing cryptographic properties..."
        let cryptoResults = [
            runPropertyTest "Hash deterministic" CryptoProperties.hashDeterministic
            runPropertyTest "Hash different inputs" CryptoProperties.hashDifferent
            runPropertyTest "Hash length" CryptoProperties.hashLength
            runPropertyTest "Signature verification" CryptoProperties.signatureVerification
            runPropertyTest "Signature invalid" CryptoProperties.signatureInvalid
        ]

        printfn "Testing staking properties..."
        let stakingResults = [
            runPropertyTest "Block reward halving" StakingProperties.blockRewardHalving
            runPropertyTest "Fee burning proportional" StakingProperties.feeBurningProportional
        ]

        printfn "Property-based tests completed!"
        let allPassed = List.forall id (cryptoResults @ stakingResults)
        if allPassed then
            printfn "All properties verified with random test cases."
        else
            printfn "One or more property checks failed."
        allPassed
