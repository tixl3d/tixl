# Backups

By default TiXL saves backups every 3 minutes. This backups are a zip archive of all your Operators and Settings. However, it will not container any resources like shaders or textures, because these are editing outside of TiXL.


## Backup locations
- In **TiXL** your backups are saved in your TiXL settings folder, located in: `<your-user-directory>/AppData/Roaming/TiXL/Backup/`
- in **Tooll3** all backups are saved in the folder `.t3/backup`. 


## How to restore a backup

Each of these Backups is a complete copy of all your Operators and Settings packed as a Zip archive. To restore it...

1. Close TiXL
2. Find the a latest backup file
3. Extract the folder
4. Copy the contents of the folder `Operator/Types/` to your copy of TiXL (e.g. `Operator/Types/`)
5. Restart TiXL.
