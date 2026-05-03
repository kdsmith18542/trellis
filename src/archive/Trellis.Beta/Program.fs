namespace Trellis.Beta

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open SimpleTypes
open SimpleCrypto

/// Beta testnet program entry point
module Program =

    [<EntryPoint>]
    let main args =
        printfn "Starting Trellis Beta Testnet..."
        
        // Build configuration
        let configuration = ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional = true)
            .AddJsonFile("appsettings.Production.json", optional = true)
            .AddEnvironmentVariables()
            .Build()
        
        // Create testnet configuration
        let testnetConfig = {
            NetworkId = "trellis-beta-testnet"
            GenesisBlock = createGenesisBlock()
            InitialValidators = []
            TestnetDuration = TimeSpan.FromDays(30.0)
            RewardPool = Balance(10000000UL) // 10M tokens
            ComplianceRequirements = {
                MinKYCScore = 0.8
                MinComplianceScore = 0.7
                MinReputation = 0.6
                MaxSlashingEvents = 2
                RequiredStake = Balance(10000UL) // 10k tokens minimum stake
            }
        }
        
        // Create host builder
        let hostBuilder = Host.CreateDefaultBuilder(args)
        
        // Configure services
        let host = hostBuilder
            .ConfigureServices(fun (context: HostBuilderContext) (services: IServiceCollection) ->
                // Add logging
                services.AddLogging(fun logging ->
                    logging.AddConsole() |> ignore
                    logging.AddDebug() |> ignore
                ) |> ignore
                
                // Add testnet coordinator
                services.AddSingleton<BetaTestnetCoordinator>(fun provider ->
                    let logger = provider.GetRequiredService<ILogger<BetaTestnetCoordinator>>()
                    new BetaTestnetCoordinator(testnetConfig, logger)
                ) |> ignore
                
                // Add corporate validator registry
                services.AddSingleton<CorporateValidatorRegistry>() |> ignore
                
                // Add incentive system
                services.AddSingleton<CorporateIncentiveSystem>(fun provider ->
                    let parameters = {
                        BaseBlockReward = Balance(1000UL)
                        ConsensusParticipationMultiplier = 1.5
                        ComplianceMultiplier = 1.2
                        ReputationMultiplier = 1.3
                        StakingMultiplier = 1.1
                        SlashingPenalty = Balance(5000UL)
                        InactivityPenalty = Balance(2000UL)
                    }
                    new CorporateIncentiveSystem(parameters)
                ) |> ignore
                
                // Add testnet background service
                services.AddHostedService<TestnetBackgroundService>() |> ignore
            )
            .Build()
        
        // Run the host
        try
            printfn "Beta Testnet started successfully"
            printfn "Network ID: %s" testnetConfig.NetworkId
            printfn "Testnet Duration: %s" (testnetConfig.TestnetDuration.ToString())
            printfn "Reward Pool: %d tokens" (match testnetConfig.RewardPool with | Balance b -> b)
            printfn "Compliance Requirements:"
            printfn "  - Min KYC Score: %.1f" testnetConfig.ComplianceRequirements.MinKYCScore
            printfn "  - Min Compliance Score: %.1f" testnetConfig.ComplianceRequirements.MinComplianceScore
            printfn "  - Min Reputation: %.1f" testnetConfig.ComplianceRequirements.MinReputation
            printfn "  - Max Slashing Events: %d" testnetConfig.ComplianceRequirements.MaxSlashingEvents
            printfn "  - Required Stake: %d tokens" (match testnetConfig.ComplianceRequirements.RequiredStake with | Balance b -> b)
            
            host.Run()
            0
        with
        | ex ->
            printfn "Beta Testnet failed to start: %s" ex.Message
            1

    /// Create genesis block for testnet
    let createGenesisBlock() : Block =
        let genesisHeader = {
            Height = BlockHeight(0UL)
            PreviousHash = Hash(Array.create 32 0uy)
            PoWHash = Hash(Array.create 32 0uy)
            Timestamp = Timestamp(DateTime.UtcNow)
            Finalized = false
        }
        
        let genesisTransaction = {
            From = Address(Array.create 20 0uy) // Genesis address
            To = Address(Array.create 20 1uy) // First beta address
            Amount = Balance(1000000UL) // 1M beta tokens
            Nonce = Nonce(0UL)
            Timestamp = Timestamp(DateTime.UtcNow)
            Signature = Signature(Array.create 64 0uy) // Genesis signature
        }
        
        {
            Header = genesisHeader
            Transactions = [genesisTransaction]
        }

/// Testnet background service
type TestnetBackgroundService(coordinator: BetaTestnetCoordinator, logger: ILogger<TestnetBackgroundService>) =
    inherit BackgroundService()
    
    override this.ExecuteAsync(stoppingToken) =
        async {
            logger.LogInformation("Starting Testnet Background Service...")
            
            // Start the testnet
            coordinator.StartTestnet() |> ignore
            
            // Keep running until cancellation
            while not stoppingToken.IsCancellationRequested do
                do! Async.Sleep(1000)
            
            // Stop the testnet
            coordinator.StopTestnet() |> ignore
            
            logger.LogInformation("Testnet Background Service stopped.")
        } |> Async.StartAsTask :> Task
