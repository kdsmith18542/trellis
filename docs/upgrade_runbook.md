# Trellis Upgrade Runbook

This runbook outlines the process for upgrading a Trellis node or network to a new version.

## Upgrade Levels

1. **Patch/Minor Upgrade**: Non-breaking changes, no state migration required.
2. **Major Upgrade / Hard Fork**: Breaking protocol changes or storage schema migrations.

## Minor Upgrade Process

1. **Stop the node**:
   ```bash
   # If using systemd
   sudo systemctl stop trellis
   ```

2. **Backup Data**:
   ```bash
   cp -r ./data ./data_backup_$(date +%F)
   ```

3. **Deploy New Binaries**:
   Overwriting existing DLLs with the new version.

4. **Restart Node**:
   ```bash
   sudo systemctl start trellis
   ```

## Hard Fork Process (Planned Upgrades)

1. **Set Hard Fork Height**:
   Configure the target block height in `TrellisConfig.current`.

2. **Distribute New Version**:
   Ensure >66% of validators have installed the new version before the fork height is reached.

3. **Monitor Transition**:
   At the fork height, ensure all nodes successfully transition to the new logic.

## Rollback Procedure

If the upgrade fails:
1. Stop the node.
2. Restore the `./data` directory from backup.
3. Revert binaries to the previous version.
4. Restart the node.
