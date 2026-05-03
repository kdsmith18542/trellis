namespace Trellis.Tests

open System
open Xunit
open Trellis.Core
open Trellis.Core.Types
open Trellis.Core.Consensus
open Trellis.Core.Crypto

/// Unit tests for consensus logic
module ConsensusTests =

    /// Create test validator set
    let createTestValidatorSet (count: int) : Set<PublicKey> =
        [1..count] |> List.map (fun _ ->
            let (_, publicKey) = Crypto.generateKeyPair()
            publicKey
        ) |> Set.ofList

    /// Create test block
    let createTestBlock (height: BlockHeight) (prevHash: Hash) : Block =
        {
            Header = {
                Height = height
                PreviousHash = prevHash
                MerkleRoot = Hash [||]
                Timestamp = Timestamp DateTime.Now
                Nonce = 0UL
                Difficulty = 1000UL
                PoWHash = Hash [||]
                ValidatorVotes = []
                Finalized = false
            }
            Transactions = []
        }

    /// Create test vote
    let createTestVote (validator: PublicKey) (blockHeight: BlockHeight) : (PublicKey * BLSSignature) =
        let message = match blockHeight with | BlockHeight h -> BitConverter.GetBytes(h)
        let signature = Crypto.generateBLSSignature (PrivateKey [||]) message
        (validator, signature)

    [<Fact>]
    let ``HasFinality returns true when sufficient votes present`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let block = createTestBlock (BlockHeight 1UL) (Hash [||])
        let votes = validatorSet |> Set.toList |> List.map (fun validator ->
            createTestVote validator (BlockHeight 1UL)
        )
        let blockWithVotes = { block with Header = { block.Header with ValidatorVotes = votes } }
        
        // Act
        let result = Consensus.hasFinality blockWithVotes validatorSet
        
        // Assert
        Assert.True(result)

    [<Fact>]
    let ``HasFinality returns false when insufficient votes present`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let block = createTestBlock (BlockHeight 1UL) (Hash [||])
        let votes = validatorSet |> Set.toList |> List.take 1 |> List.map (fun validator ->
            createTestVote validator (BlockHeight 1UL)
        )
        let blockWithVotes = { block with Header = { block.Header with ValidatorVotes = votes } }
        
        // Act
        let result = Consensus.hasFinality blockWithVotes validatorSet
        
        // Assert
        Assert.False(result)

    [<Fact>]
    let ``ValidateVote returns true for valid vote`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let validator = validatorSet |> Set.toList |> List.head
        let blockHeight = BlockHeight 1UL
        let vote = createTestVote validator blockHeight
        
        // Act
        let result = Consensus.validateVote vote blockHeight validatorSet
        
        // Assert
        Assert.True(result)

    [<Fact>]
    let ``ValidateVote returns false for invalid validator`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let (_, invalidValidator) = Crypto.generateKeyPair()
        let blockHeight = BlockHeight 1UL
        let vote = createTestVote invalidValidator blockHeight
        
        // Act
        let result = Consensus.validateVote vote blockHeight validatorSet
        
        // Assert
        Assert.False(result)

    [<Fact>]
    let ``ProcessVotes adds valid votes to block`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let block = createTestBlock (BlockHeight 1UL) (Hash [||])
        let votes = validatorSet |> Set.toList |> List.map (fun validator ->
            createTestVote validator (BlockHeight 1UL)
        )
        
        // Act
        let (updatedBlock, finalized) = Consensus.processVotes block votes validatorSet
        
        // Assert
        Assert.Equal(votes.Length, updatedBlock.Header.ValidatorVotes.Length)
        Assert.False(finalized) // Not enough votes for finality

    [<Fact>]
    let ``ProcessVotes finalizes block with sufficient votes`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let block = createTestBlock (BlockHeight 1UL) (Hash [||])
        let votes = validatorSet |> Set.toList |> List.map (fun validator ->
            createTestVote validator (BlockHeight 1UL)
        )
        
        // Act
        let (updatedBlock, finalized) = Consensus.processVotes block votes validatorSet
        
        // Assert
        Assert.True(finalized)
        Assert.True(updatedBlock.Header.Finalized)

    [<Fact>]
    let ``AggregateVotes combines multiple signatures`` () =
        // Arrange
        let signatures = [
            BLSSignature [|1uy; 2uy; 3uy|]
            BLSSignature [|4uy; 5uy; 6uy|]
            BLSSignature [|7uy; 8uy; 9uy|]
        ]
        
        // Act
        let aggregated = Consensus.aggregateVotes (signatures |> List.map (fun sig -> (PublicKey [||], sig)))
        
        // Assert
        Assert.NotNull(aggregated)

    [<Fact>]
    let ``ShouldSlashValidator returns true for double signing`` () =
        // Arrange
        let validator = PublicKey [||]
        let recentVotes = [
            (validator, BLSSignature [||], BlockHeight 1UL)
            (validator, BLSSignature [||], BlockHeight 1UL) // Double vote at same height
        ]
        
        // Act
        let result = Consensus.shouldSlashValidator validator recentVotes
        
        // Assert
        Assert.True(result)

    [<Fact>]
    let ``ShouldSlashValidator returns false for normal voting`` () =
        // Arrange
        let validator = PublicKey [||]
        let recentVotes = [
            (validator, BLSSignature [||], BlockHeight 1UL)
            (validator, BLSSignature [||], BlockHeight 2UL)
        ]
        
        // Act
        let result = Consensus.shouldSlashValidator validator recentVotes
        
        // Assert
        Assert.False(result)

    [<Fact>]
    let ``CalculateValidatorRewards distributes rewards evenly`` () =
        // Arrange
        let block = createTestBlock (BlockHeight 1UL) (Hash [||])
        let validatorSet = createTestValidatorSet 3
        
        // Act
        let rewards = Consensus.calculateValidatorRewards block validatorSet
        
        // Assert
        Assert.Equal(3, rewards.Count)
        let rewardValues = rewards |> Map.toList |> List.map snd
        let firstReward = rewardValues.Head
        Assert.True(rewardValues |> List.forall (fun reward -> reward = firstReward))

    [<Fact>]
    let ``UpdateValidatorSet removes slashed validators`` () =
        // Arrange
        let currentSet = createTestValidatorSet 3
        let slashedValidator = currentSet |> Set.toList |> List.head
        let slashedValidators = [slashedValidator]
        let newValidators = []
        
        // Act
        let updatedSet = Consensus.updateValidatorSet currentSet newValidators slashedValidators
        
        // Assert
        Assert.False(updatedSet.Contains(slashedValidator))
        Assert.Equal(2, updatedSet.Count)

    [<Fact>]
    let ``UpdateValidatorSet adds new validators`` () =
        // Arrange
        let currentSet = createTestValidatorSet 3
        let (_, newValidator) = Crypto.generateKeyPair()
        let newValidators = [newValidator]
        let slashedValidators = []
        
        // Act
        let updatedSet = Consensus.updateValidatorSet currentSet newValidators slashedValidators
        
        // Assert
        Assert.True(updatedSet.Contains(newValidator))
        Assert.Equal(4, updatedSet.Count)

    [<Fact>]
    let ``IsConsensusTimeout returns true after timeout`` () =
        // Arrange
        let blockHeight = BlockHeight 1UL
        let lastBlockTime = Timestamp (DateTime.Now.AddSeconds(-35))
        let timeoutSeconds = 30
        
        // Act
        let result = Consensus.isConsensusTimeout blockHeight lastBlockTime timeoutSeconds
        
        // Assert
        Assert.True(result)

    [<Fact>]
    let ``IsConsensusTimeout returns false before timeout`` () =
        // Arrange
        let blockHeight = BlockHeight 1UL
        let lastBlockTime = Timestamp (DateTime.Now.AddSeconds(-15))
        let timeoutSeconds = 30
        
        // Act
        let result = Consensus.isConsensusTimeout blockHeight lastBlockTime timeoutSeconds
        
        // Assert
        Assert.False(result)

    [<Fact>]
    let ``GenerateConsensusMessage creates valid message`` () =
        // Arrange
        let block = createTestBlock (BlockHeight 1UL) (Hash [||])
        let validatorSet = createTestValidatorSet 3
        
        // Act
        let message = Consensus.generateConsensusMessage block validatorSet
        
        // Assert
        Assert.NotNull(message)

    [<Fact>]
    let ``ProcessConsensusMessage accepts valid message`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let validator = validatorSet |> Set.toList |> List.head
        let message = {
            Validator = validator
            BlockHeight = BlockHeight 1UL
            BlockHash = Hash [||]
            ValidatorSet = validatorSet
            Timestamp = Timestamp DateTime.Now
            Signature = BLSSignature [||]
        }
        
        // Act
        let result = Consensus.processConsensusMessage message validatorSet
        
        // Assert
        Assert.Equal(ConsensusResult.Accepted, result)

    [<Fact>]
    let ``ProcessConsensusMessage rejects invalid validator`` () =
        // Arrange
        let validatorSet = createTestValidatorSet 3
        let (_, invalidValidator) = Crypto.generateKeyPair()
        let message = {
            Validator = invalidValidator
            BlockHeight = BlockHeight 1UL
            BlockHash = Hash [||]
            ValidatorSet = validatorSet
            Timestamp = Timestamp DateTime.Now
            Signature = BLSSignature [||]
        }
        
        // Act
        let result = Consensus.processConsensusMessage message validatorSet
        
        // Assert
        Assert.Equal(ConsensusResult.Rejected "Invalid validator", result)
