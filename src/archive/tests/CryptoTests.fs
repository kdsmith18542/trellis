namespace Trellis.Tests

open System
open Xunit
open Trellis.Core
open Trellis.Core.Types
open Trellis.Core.Crypto

/// Unit tests for cryptographic functions
module CryptoTests =

    [<Fact>]
    let ``GenerateKeyPair creates valid key pair`` () =
        // Act
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        
        // Assert
        Assert.NotNull(privateKey)
        Assert.NotNull(publicKey)
        let (PrivateKey privBytes) = privateKey
        let (PublicKey pubBytes) = publicKey
        Assert.True(privBytes.Length > 0)
        Assert.True(pubBytes.Length > 0)

    [<Fact>]
    let ``SHA256 produces consistent hashes`` () =
        // Arrange
        let data = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        
        // Act
        let hash1 = Crypto.sha256 data
        let hash2 = Crypto.sha256 data
        
        // Assert
        Assert.Equal(hash1, hash2)

    [<Fact>]
    let ``SHA256 produces different hashes for different data`` () =
        // Arrange
        let data1 = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        let data2 = [|1uy; 2uy; 3uy; 4uy; 6uy|]
        
        // Act
        let hash1 = Crypto.sha256 data1
        let hash2 = Crypto.sha256 data2
        
        // Assert
        Assert.NotEqual(hash1, hash2)

    [<Fact>]
    let ``DoubleSHA256 produces consistent hashes`` () =
        // Arrange
        let data = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        
        // Act
        let hash1 = Crypto.doubleSha256 data
        let hash2 = Crypto.doubleSha256 data
        
        // Assert
        Assert.Equal(hash1, hash2)

    [<Fact>]
    let ``Sign and verify signature works correctly`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let data = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        
        // Act
        let signature = Crypto.sign privateKey data
        let isValid = Crypto.verifySignature publicKey data signature
        
        // Assert
        Assert.True(isValid)

    [<Fact>]
    let ``Sign and verify signature fails with wrong data`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let data1 = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        let data2 = [|1uy; 2uy; 3uy; 4uy; 6uy|]
        
        // Act
        let signature = Crypto.sign privateKey data1
        let isValid = Crypto.verifySignature publicKey data2 signature
        
        // Assert
        Assert.False(isValid)

    [<Fact>]
    let ``Sign and verify signature fails with wrong public key`` () =
        // Arrange
        let (privateKey, _) = Crypto.generateKeyPair()
        let (_, wrongPublicKey) = Crypto.generateKeyPair()
        let data = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        
        // Act
        let signature = Crypto.sign privateKey data
        let isValid = Crypto.verifySignature wrongPublicKey data signature
        
        // Assert
        Assert.False(isValid)

    [<Fact>]
    let ``BLS signature generation and verification works`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let message = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        
        // Act
        let signature = Crypto.generateBLSSignature privateKey message
        let isValid = Crypto.verifyBLSSignature publicKey message signature
        
        // Assert
        Assert.True(isValid)

    [<Fact>]
    let ``BLS signature verification fails with wrong message`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let message1 = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        let message2 = [|1uy; 2uy; 3uy; 4uy; 6uy|]
        
        // Act
        let signature = Crypto.generateBLSSignature privateKey message1
        let isValid = Crypto.verifyBLSSignature publicKey message2 signature
        
        // Assert
        Assert.False(isValid)

    [<Fact>]
    let ``BLS signature aggregation works`` () =
        // Arrange
        let signatures = [
            BLSSignature [|1uy; 2uy; 3uy|]
            BLSSignature [|4uy; 5uy; 6uy|]
            BLSSignature [|7uy; 8uy; 9uy|]
        ]
        
        // Act
        let aggregated = Crypto.aggregateBLSSignatures signatures
        
        // Assert
        Assert.NotNull(aggregated)

    [<Fact>]
    let ``Address generation is deterministic`` () =
        // Arrange
        let publicKey = PublicKey [|1uy; 2uy; 3uy; 4uy; 5uy|]
        
        // Act
        let address1 = Crypto.addressFromPublicKey publicKey
        let address2 = Crypto.addressFromPublicKey publicKey
        
        // Assert
        Assert.Equal(address1, address2)

    [<Fact>]
    let ``Transaction hash is deterministic`` () =
        // Arrange
        let tx = {
            From = Address [|1uy; 2uy; 3uy|]
            To = Address [|4uy; 5uy; 6uy|]
            Amount = Balance 100UL
            Nonce = Nonce 1UL
            GasLimit = 21000UL
            GasPrice = 20UL
            Data = None
            Signature = Signature [||]
            Metadata = None
        }
        
        // Act
        let hash1 = Crypto.transactionHash tx
        let hash2 = Crypto.transactionHash tx
        
        // Assert
        Assert.Equal(hash1, hash2)

    [<Fact>]
    let ``Block hash is deterministic`` () =
        // Arrange
        let header = {
            Height = BlockHeight 1UL
            PreviousHash = Hash [|1uy; 2uy; 3uy|]
            MerkleRoot = Hash [|4uy; 5uy; 6uy|]
            Timestamp = Timestamp DateTime.Now
            Nonce = 123UL
            Difficulty = 1000UL
            PoWHash = Hash [||]
            ValidatorVotes = []
            Finalized = false
        }
        
        // Act
        let hash1 = Crypto.blockHash header
        let hash2 = Crypto.blockHash header
        
        // Assert
        Assert.Equal(hash1, hash2)

    [<Fact>]
    let ``MeetsDifficulty returns true for easy difficulty`` () =
        // Arrange
        let hash = Hash [|0uy; 0uy; 0uy; 0uy|] // Very easy hash
        let difficulty = 1UL
        
        // Act
        let result = Crypto.meetsDifficulty hash difficulty
        
        // Assert
        Assert.True(result)

    [<Fact>]
    let ``MeetsDifficulty returns false for hard difficulty`` () =
        // Arrange
        let hash = Hash [|255uy; 255uy; 255uy; 255uy|] // Very hard hash
        let difficulty = 1000000UL
        
        // Act
        let result = Crypto.meetsDifficulty hash difficulty
        
        // Assert
        Assert.False(result)

    [<Fact>]
    let ``Transaction verification works for valid transaction`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let tx = {
            From = Address [|1uy; 2uy; 3uy|]
            To = Address [|4uy; 5uy; 6uy|]
            Amount = Balance 100UL
            Nonce = Nonce 1UL
            GasLimit = 21000UL
            GasPrice = 20UL
            Data = None
            Signature = Signature [||]
            Metadata = None
        }
        
        // Act
        let result = Crypto.verifyTransaction tx
        
        // Assert
        Assert.True(result)

    [<Fact>]
    let ``Hash functions produce different results for different inputs`` () =
        // Arrange
        let data1 = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        let data2 = [|1uy; 2uy; 3uy; 4uy; 6uy|]
        
        // Act
        let hash1 = Crypto.sha256 data1
        let hash2 = Crypto.sha256 data2
        let doubleHash1 = Crypto.doubleSha256 data1
        let doubleHash2 = Crypto.doubleSha256 data2
        
        // Assert
        Assert.NotEqual(hash1, hash2)
        Assert.NotEqual(doubleHash1, doubleHash2)
        Assert.NotEqual(hash1, doubleHash1)
        Assert.NotEqual(hash2, doubleHash2)

    [<Fact>]
    let ``Key pair generation produces unique keys`` () =
        // Arrange & Act
        let (priv1, pub1) = Crypto.generateKeyPair()
        let (priv2, pub2) = Crypto.generateKeyPair()
        
        // Assert
        Assert.NotEqual(priv1, priv2)
        Assert.NotEqual(pub1, pub2)

    [<Fact>]
    let ``Signature generation produces unique signatures`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let data1 = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        let data2 = [|1uy; 2uy; 3uy; 4uy; 6uy|]
        
        // Act
        let sig1 = Crypto.sign privateKey data1
        let sig2 = Crypto.sign privateKey data2
        
        // Assert
        Assert.NotEqual(sig1, sig2)

    [<Fact>]
    let ``BLS signature generation produces unique signatures`` () =
        // Arrange
        let (privateKey, publicKey) = Crypto.generateKeyPair()
        let message1 = [|1uy; 2uy; 3uy; 4uy; 5uy|]
        let message2 = [|1uy; 2uy; 3uy; 4uy; 6uy|]
        
        // Act
        let sig1 = Crypto.generateBLSSignature privateKey message1
        let sig2 = Crypto.generateBLSSignature privateKey message2
        
        // Assert
        Assert.NotEqual(sig1, sig2)
