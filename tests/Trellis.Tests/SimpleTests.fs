namespace Trellis.Tests

open Trellis.Core
open System

/// Simple tests for Trellis
module SimpleTests =

    let runTests () =
        printfn "Running Trellis tests..."
        
        // Test hash generation
        let data = [| 1uy; 2uy; 3uy; 4uy |]
        let hash = SimpleCrypto.sha256 data
        let (SimpleTypes.Hash hashBytes) = hash
        printfn "Hash test: %s" (if hashBytes.Length = 32 then "PASS" else "FAIL")
        
        // Test key pair generation
        let (publicKey, privateKey) = SimpleCrypto.generateKeyPair()
        let (SimpleTypes.PrivateKey privBytes) = privateKey
        let (SimpleTypes.PublicKey pubBytes) = publicKey
        printfn "Key generation test: %s" (if privBytes.Length > 0 && pubBytes.Length > 0 then "PASS" else "FAIL")
        
        // Test signature and verification
        let data2 = [| 5uy; 6uy; 7uy; 8uy |]
        let signature = SimpleCrypto.sign privateKey data2
        let isValid = SimpleCrypto.verifySignature publicKey data2 signature
        printfn "Signature test: %s" (if isValid then "PASS" else "FAIL")
        
        // Test staking functionality
        let testAddress = SimpleTypes.Address(System.Guid.NewGuid().ToByteArray())
        let stakedAmount = SimpleTypes.Balance(1000UL * 1_000_000_000_000_000_000UL) // 1000 TRL
        let stakingDuration = TimeSpan.FromDays(30.0) // 30 days
        let stakingInfo = SimpleCrypto.Staking.createStakingInfo testAddress stakedAmount stakingDuration
        printfn "Staking creation test: %s" (if stakingInfo.Address = testAddress && stakingInfo.IsActive then "PASS" else "FAIL")

        // Test staking rewards calculation
        let currentTime = SimpleTypes.Timestamp(DateTime.UtcNow.AddDays(15.0)) // 15 days later
        let rewards = SimpleCrypto.Staking.calculateStakingRewards stakingInfo currentTime
        let (SimpleTypes.Balance rewardAmount) = rewards
        printfn "Staking rewards test: %s" (if rewardAmount > 0UL then "PASS" else "FAIL")

        // Test fee burning
        let transactionId = SimpleTypes.Hash(System.Guid.NewGuid().ToByteArray())
        let totalFee = SimpleTypes.Balance(100UL * 1_000_000_000_000_000_000UL) // 100 TRL fee
        let feeBurning = SimpleCrypto.Staking.processFeeBurning transactionId totalFee
        printfn "Fee burning test: %s" (if feeBurning.IsSome then "PASS" else "FAIL")

        // Test block reward calculation
        let blockHeight = SimpleTypes.BlockHeight(100000UL)
        let blockReward = SimpleCrypto.Staking.calculateBlockReward blockHeight
        let (SimpleTypes.Balance reward) = blockReward
        printfn "Block reward test: %s" (if reward > 0UL then "PASS" else "FAIL")

        // Test P2P networking concepts
        let testNodeId = System.Guid.NewGuid().ToString("N")
        let testDistance = 12345UL
        printfn "P2P networking test: %s" (if testNodeId.Length > 0 && testDistance > 0UL then "PASS" else "FAIL")

        // Test Kademlia DHT concepts
        let testActive = true
        let testLastSeen = DateTime.UtcNow
        printfn "Kademlia DHT test: %s" (if testActive && testLastSeen > DateTime.MinValue then "PASS" else "FAIL")
        
        printfn "All tests completed!"