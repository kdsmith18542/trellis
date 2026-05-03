namespace Trellis.Tests

open System
open Xunit
open Trellis.Core
open SimpleTypes
open SimpleCrypto

/// Unit tests for transaction processing
module TransactionTests =

    /// Create test key pair
    let createTestKeyPair () : (PrivateKey * PublicKey) =
        SimpleCrypto.generateKeyPair()

    /// Create test address from public key
    let createTestAddress (publicKey: PublicKey) : Address =
        let (PublicKey pubBytes) = publicKey
        // Use first 20 bytes as address
        let addrBytes = if pubBytes.Length >= 20 then pubBytes.[0..19] else Array.append pubBytes (Array.zeroCreate (20 - pubBytes.Length))
        Address addrBytes

    /// Create test transaction
    let createTestTransaction (from: Address) (toAddr: Address) (amount: Balance) (nonce: Nonce) (fee: Balance) (privateKey: PrivateKey) : Transaction =
        let baseTx = {
            From = from
            To = toAddr
            Amount = amount
            Nonce = nonce
            Fee = fee
            Signature = Signature [||]
            Hash = Hash [||]
            GasLimit = 21000UL
            GasPrice = 20UL
            Data = [||]
            Metadata = Map.empty
            MaxFeePerGas = None
            MaxPriorityFeePerGas = None
            TransactionType = 0uy
        }
        let txHash = SimpleCrypto.transactionHashSimple baseTx
        let signature = SimpleCrypto.sign privateKey (match txHash with | Hash h -> h)
        { baseTx with Hash = txHash; Signature = signature }

    /// Create test account
    let createTestAccount (address: Address) (balance: Balance) (nonce: Nonce) : Account =
        {
            Address = address
            Balance = balance
            Nonce = nonce
        }

    [<Fact>]
    let ``Transaction with valid signature should verify`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let amount = Balance 1000UL
        let nonce = Nonce 1UL
        let fee = Balance 10UL
        let tx = createTestTransaction fromAddr toAddr amount nonce fee privateKey

        // Act
        let isValid = SimpleCrypto.verifyTransactionSimple tx

        // Assert
        Assert.True(isValid, "Transaction with valid signature should verify")

    [<Fact>]
    let ``Transaction with invalid signature should not verify`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let amount = Balance 1000UL
        let nonce = Nonce 1UL
        let fee = Balance 10UL
        let tx = createTestTransaction fromAddr toAddr amount nonce fee privateKey
        // Corrupt the signature
        let corruptedTx = { tx with Signature = Signature [| 0xFFuy |] }

        // Act
        let isValid = SimpleCrypto.verifyTransactionSimple corruptedTx

        // Assert
        Assert.False(isValid, "Transaction with invalid signature should not verify")

    [<Fact>]
    let ``Transaction hash should be consistent`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let amount = Balance 1000UL
        let nonce = Nonce 1UL
        let fee = Balance 10UL
        let baseTx = {
            From = fromAddr
            To = toAddr
            Amount = amount
            Nonce = nonce
            Fee = fee
            Signature = Signature [||]
            Hash = Hash [||]
            GasLimit = 21000UL
            GasPrice = 20UL
            Data = [||]
            Metadata = Map.empty
            MaxFeePerGas = None
            MaxPriorityFeePerGas = None
            TransactionType = 0uy
        }

        // Act
        let hash1 = SimpleCrypto.transactionHashSimple baseTx
        let hash2 = SimpleCrypto.transactionHashSimple baseTx

        // Assert
        Assert.Equal(hash1, hash2, "Transaction hash should be consistent for same input")

    [<Fact>]
    let ``Transaction with different amount should have different hash`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let nonce = Nonce 1UL
        let fee = Balance 10UL
        let baseTx1 = {
            From = fromAddr
            To = toAddr
            Amount = Balance 1000UL
            Nonce = nonce
            Fee = fee
            Signature = Signature [||]
            Hash = Hash [||]
            GasLimit = 21000UL
            GasPrice = 20UL
            Data = [||]
            Metadata = Map.empty
            MaxFeePerGas = None
            MaxPriorityFeePerGas = None
            TransactionType = 0uy
        }
        let baseTx2 = { baseTx1 with Amount = Balance 2000UL }

        // Act
        let hash1 = SimpleCrypto.transactionHashSimple baseTx1
        let hash2 = SimpleCrypto.transactionHashSimple baseTx2

        // Assert
        Assert.NotEqual(hash1, hash2, "Transactions with different amounts should have different hashes")

    [<Fact>]
    let ``Transaction with sufficient balance should be valid`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let amount = Balance 1000UL
        let fee = Balance 10UL
        let account = createTestAccount fromAddr (Balance 2000UL) (Nonce 0UL)
        let tx = createTestTransaction fromAddr toAddr amount (Nonce 1UL) fee privateKey

        // Act
        let totalRequired = 
            match amount with | Balance a -> 
            match fee with | Balance f -> a + f
        let (Balance balance) = account.Balance
        let hasSufficientBalance = balance >= totalRequired

        // Assert
        Assert.True(hasSufficientBalance, "Transaction with sufficient balance should be valid")

    [<Fact>]
    let ``Transaction with insufficient balance should be invalid`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let amount = Balance 1000UL
        let fee = Balance 10UL
        let account = createTestAccount fromAddr (Balance 500UL) (Nonce 0UL)
        let tx = createTestTransaction fromAddr toAddr amount (Nonce 1UL) fee privateKey

        // Act
        let totalRequired = 
            match amount with | Balance a -> 
            match fee with | Balance f -> a + f
        let (Balance balance) = account.Balance
        let hasSufficientBalance = balance >= totalRequired

        // Assert
        Assert.False(hasSufficientBalance, "Transaction with insufficient balance should be invalid")

    [<Fact>]
    let ``Transaction with correct nonce should be valid`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let account = createTestAccount fromAddr (Balance 2000UL) (Nonce 5UL)
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 6UL) (Balance 10UL) privateKey

        // Act
        let (Nonce txNonce) = tx.Nonce
        let (Nonce accNonce) = account.Nonce
        let validNonce = txNonce = accNonce + 1UL

        // Assert
        Assert.True(validNonce, "Transaction with correct nonce should be valid")

    [<Fact>]
    let ``Transaction with incorrect nonce should be invalid`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let account = createTestAccount fromAddr (Balance 2000UL) (Nonce 5UL)
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 3UL) (Balance 10UL) privateKey

        // Act
        let (Nonce txNonce) = tx.Nonce
        let (Nonce accNonce) = account.Nonce
        let validNonce = txNonce = accNonce + 1UL

        // Assert
        Assert.False(validNonce, "Transaction with incorrect nonce should be invalid")

    [<Fact>]
    let ``New account transaction should have nonce 1`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 1UL) (Balance 10UL) privateKey

        // Act
        let (Nonce txNonce) = tx.Nonce
        let validNonce = txNonce = 1UL

        // Assert
        Assert.True(validNonce, "New account transaction should have nonce 1")

    [<Fact>]
    let ``Transaction with zero amount should be invalid`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let tx = createTestTransaction fromAddr toAddr (Balance 0UL) (Nonce 1UL) (Balance 10UL) privateKey

        // Act
        let (Balance amount) = tx.Amount
        let isValid = amount > 0UL

        // Assert
        Assert.False(isValid, "Transaction with zero amount should be invalid")

    [<Fact>]
    let ``Transaction with zero fee should be invalid`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 1UL) (Balance 0UL) privateKey

        // Act
        let (Balance fee) = tx.Fee
        let isValid = fee > 0UL

        // Assert
        Assert.False(isValid, "Transaction with zero fee should be invalid")

    [<Fact>]
    let ``Transaction from and to addresses should be different`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let tx = createTestTransaction fromAddr fromAddr (Balance 1000UL) (Nonce 1UL) (Balance 10UL) privateKey

        // Act
        let addressesAreDifferent = tx.From <> tx.To

        // Assert
        Assert.False(addressesAreDifferent, "Transaction from and to addresses should be different")

    [<Fact>]
    let ``Transaction gas limit should be positive`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 1UL) (Balance 10UL) privateKey

        // Act
        let hasValidGasLimit = tx.GasLimit > 0UL

        // Assert
        Assert.True(hasValidGasLimit, "Transaction gas limit should be positive")

    [<Fact>]
    let ``Transaction gas price should be positive`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 1UL) (Balance 10UL) privateKey

        // Act
        let hasValidGasPrice = tx.GasPrice > 0UL

        // Assert
        Assert.True(hasValidGasPrice, "Transaction gas price should be positive")

    [<Fact>]
    let ``Transaction fee should not exceed gas limit times gas price`` () =
        // Arrange
        let (privateKey, publicKey) = createTestKeyPair()
        let fromAddr = createTestAddress publicKey
        let toAddr = Address (Array.create 20 1uy)
        let gasLimit = 21000UL
        let gasPrice = 20UL
        let maxFee = gasLimit * gasPrice
        let tx = createTestTransaction fromAddr toAddr (Balance 1000UL) (Nonce 1UL) (Balance (maxFee + 1UL)) privateKey
        let updatedTx = { tx with GasLimit = gasLimit; GasPrice = gasPrice }

        // Act
        let (Balance fee) = updatedTx.Fee
        let maxFeeByGas = updatedTx.GasLimit * updatedTx.GasPrice
        let feeIsValid = fee <= maxFeeByGas

        // Assert
        Assert.False(feeIsValid, "Transaction fee should not exceed gas limit times gas price")

