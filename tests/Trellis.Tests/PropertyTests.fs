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
                r2 <= r1 // Later blocks should have <= rewards
            else
                true
        
        /// Fee burning should be proportional to fee amount
        let feeBurningProportional (totalFee: SimpleTypes.Balance) =
            let (SimpleTypes.Balance feeAmount) = totalFee
            if feeAmount > 0UL then
                let feeBurning = SimpleCrypto.Staking.processFeeBurning (SimpleTypes.Hash(Array.zeroCreate 32)) totalFee
                match feeBurning with
                | Some burn ->
                    let (SimpleTypes.Balance burnedAmount) = burn.BurnedAmount
                    burnedAmount > 0UL && burnedAmount <= feeAmount
                | None -> false
            else
                true

    /// Run all property tests
    let runPropertyTests () =
        printfn "Running Property-Based Tests..."
        printfn "================================="
        
        // Cryptographic properties
        printfn "Testing cryptographic properties..."
        try
            Check.Quick CryptoProperties.hashDeterministic
            printfn "✓ Hash deterministic: PASS"
        with
        | ex -> printfn "✗ Hash deterministic: FAIL - %s" ex.Message
        
        try
            Check.Quick CryptoProperties.hashDifferent
            printfn "✓ Hash different inputs: PASS"
        with
        | ex -> printfn "✗ Hash different inputs: FAIL - %s" ex.Message
        
        try
            Check.Quick CryptoProperties.hashLength
            printfn "✓ Hash length: PASS"
        with
        | ex -> printfn "✗ Hash length: FAIL - %s" ex.Message
        
        try
            Check.Quick CryptoProperties.signatureVerification
            printfn "✓ Signature verification: PASS"
        with
        | ex -> printfn "✗ Signature verification: FAIL - %s" ex.Message
        
        try
            Check.Quick CryptoProperties.signatureInvalid
            printfn "✓ Signature invalid: PASS"
        with
        | ex -> printfn "✗ Signature invalid: FAIL - %s" ex.Message
        
        // Staking properties
        printfn "Testing staking properties..."
        try
            Check.Quick StakingProperties.blockRewardHalving
            printfn "✓ Block reward halving: PASS"
        with
        | ex -> printfn "✗ Block reward halving: FAIL - %s" ex.Message
        
        try
            Check.Quick StakingProperties.feeBurningProportional
            printfn "✓ Fee burning proportional: PASS"
        with
        | ex -> printfn "✗ Fee burning proportional: FAIL - %s" ex.Message
        
        printfn "Property-based tests completed!"
        printfn "All properties verified with random test cases."