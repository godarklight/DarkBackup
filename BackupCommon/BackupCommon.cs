using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
namespace BackupCommon
{
    public class BackupCommon
    {
        public long restoreID;
        private string backupPath;
        private string restorePath;
        private SHA256Managed sha256 = new SHA256Managed();
        private List<string> safeDeleteList = new List<string>();
        private List<string> ignoreList = new List<string>();
        private Action<string> debugLogger;
        private Action<string> normalLogger;
        private Action<string> errorLogger;
        private Action shutdownCommand;

        public BackupCommon(string backupPath, string restorePath, Action<string> debugLogger, Action<string> normalLogger, Action<string> errorLogger, Action shutdownCommand, List<string> safeDeleteList, List<string> ignoreList)
        {
            this.backupPath = backupPath;
            this.restorePath = restorePath;
            this.debugLogger = debugLogger;
            this.normalLogger = normalLogger;
            this.errorLogger = errorLogger;
            this.shutdownCommand = shutdownCommand;
            this.safeDeleteList = safeDeleteList;
            this.ignoreList = ignoreList;
            Directory.CreateDirectory(backupPath);
            Directory.CreateDirectory(Path.Combine(backupPath, "Objects"));
            Directory.CreateDirectory(Path.Combine(backupPath, "Index"));
        }

        public string GetSHA256Hash(byte[] input)
        {

            byte[] hash = sha256.ComputeHash(input);
            string hashString = string.Empty;
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }
            return hashString;
        }

        public void BackupUniverse(long backupID)
        {
            int totalFilesBackedUp = 0;
            int newFilesBackedUp = 0;
            string[] universeFiles = Directory.GetFiles(restorePath, "*", SearchOption.AllDirectories);
            Dictionary<string, string> backupFiles = new Dictionary<string, string>();
            foreach (string universeFile in universeFiles)
            {
                bool skipFile = false;
                string cropUniverseFile = universeFile.Substring(restorePath.Length + 1);
                foreach (string ignoreString in ignoreList)
                {
                    if (cropUniverseFile.StartsWith(ignoreString, StringComparison.InvariantCultureIgnoreCase))
                    {
                        skipFile = true;
                        break;
                    }
                }
                if (!skipFile)
                {
                    byte[] fileBytes = File.ReadAllBytes(universeFile);
                    string fileSha = GetSHA256Hash(fileBytes);
                    string backupName = Path.Combine(backupPath, "Objects", fileSha + ".txt");
                    backupFiles.Add(cropUniverseFile, fileSha);
                    totalFilesBackedUp++;
                    if (!File.Exists(backupName))
                    {
                        File.Copy(universeFile, backupName);
                        newFilesBackedUp++;
                        //debugLogger("Backing up: " + cropUniverseFile);
                    }
                }
            }
            using (StreamWriter sw = new StreamWriter(Path.Combine(backupPath, "Index", backupID + ".txt")))
            {
                foreach (KeyValuePair<string, string> kvp in backupFiles)
                {
                    sw.WriteLine(kvp.Key + ":" + kvp.Value);
                }
            }
            debugLogger("Plugin backing up universe, new: " + newFilesBackedUp + ", total: " + totalFilesBackedUp + ", id: " + backupID);
        }

        public void RestoreUniverse(long backupID)
        {
            if (!File.Exists(Path.Combine(backupPath, "Index", backupID + ".txt")))
            {
                normalLogger("Cannot restore to time that does not exist!");
                return;
            }
            else
            {
                long beforeRestoreID = DateTime.UtcNow.Ticks;
                debugLogger("To restore the universe before this backup, use ID: " + beforeRestoreID);
                BackupUniverse(beforeRestoreID);
                string[] oldFiles = Directory.GetFiles(restorePath, "*", SearchOption.AllDirectories);
                string[] newFiles = File.ReadAllLines(Path.Combine(backupPath, "Index", backupID + ".txt"));
                foreach (string oldFile in oldFiles)
                {
                    string cropOldFile = oldFile.Substring(restorePath.Length + 1);
                    foreach (string safeStartText in safeDeleteList)
                    {
                        if (cropOldFile.StartsWith(safeStartText, StringComparison.InvariantCultureIgnoreCase))
                        {
                            debugLogger("Deleting: " + cropOldFile);
                            File.Delete(oldFile);
                            continue;
                        }
                    }
                }
                foreach (string unsplitRestoreLine in newFiles)
                {
                    int splitIndex = unsplitRestoreLine.LastIndexOf(":", StringComparison.InvariantCultureIgnoreCase);
                    string pathPart = unsplitRestoreLine.Substring(0, splitIndex);
                    string hash = unsplitRestoreLine.Substring(splitIndex + 1);
                    debugLogger("Restoring: " + pathPart + " from " + hash + ".txt");
                    string backupLocation = Path.Combine(backupPath, "Objects", hash + ".txt");
                    string realLocation = Path.Combine(restorePath, pathPart);
                    if (File.Exists(backupLocation))
                    {
                        if (!File.Exists(realLocation))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(realLocation));
                            File.Copy(backupLocation, realLocation);
                        }
                        else
                        {
                            errorLogger(realLocation + " already exists, should be replaced by object " + hash + ".txt");
                        }
                    }
                    else
                    {
                        errorLogger("Object " + hash + ".txt is missing for " + realLocation);
                    }
                }
            }
        }

        public void RestoreCommand(string backupText)
        {
            if (backupText == "")
            {
                normalLogger("There is 3 ways to select the backup");
                string darkorluna = Path.GetFileName(backupPath);
                normalLogger("1) The exact backup given in Universe/" + darkorluna + "/Index");
                normalLogger("2) The closest backup given to a date in the format yyyy-mm-dd HH-MM-SS: 2019-3-14 1:59PM (use capital AM or PM)");
                normalLogger("2) The closest backup given to a relative time ago (xd xh xm xs): 1d 2h");
            }
            if (long.TryParse(backupText, out long backupID))
            {
                restoreID = backupID;
            }
            else
            {


                if (backupText.Contains("d") || backupText.Contains("h") || backupText.Contains("m") || backupText.Contains("s"))
                {
                    normalLogger("Relative parsed!");
                    DateTime relativeTime = DateTime.UtcNow;
                    string[] relativeSplit = backupText.Split(' ');
                    foreach (string relativeSplitPart in relativeSplit)
                    {
                        if (relativeSplitPart.Contains("d"))
                        {
                            string numberPart = relativeSplitPart.Replace("d", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddDays(-numberPartDouble);
                            }
                        }
                        if (relativeSplitPart.Contains("h"))
                        {
                            string numberPart = relativeSplitPart.Replace("h", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddHours(-numberPartDouble);
                            }
                        }
                        if (relativeSplitPart.Contains("m"))
                        {
                            string numberPart = relativeSplitPart.Replace("m", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddMinutes(-numberPartDouble);
                            }
                        }
                        if (relativeSplitPart.Contains("s"))
                        {
                            string numberPart = relativeSplitPart.Replace("s", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddSeconds(-numberPartDouble);
                            }
                        }
                    }
                    restoreID = GetClosestBackup(relativeTime);
                }
                else
                {
                    if (DateTime.TryParse(backupText, out DateTime targetDateTime))
                    {
                        if (targetDateTime.Kind == DateTimeKind.Unspecified)
                        {
                            targetDateTime = new DateTime(targetDateTime.Ticks, DateTimeKind.Local);
                        }
                        restoreID = GetClosestBackup(targetDateTime.ToUniversalTime());
                    }
                }
            }
            if (restoreID != 0)
            {
                shutdownCommand();
            }
            else
            {
                normalLogger("No backup found");
            }
        }

        private long GetClosestBackup(DateTime targetDateTime)
        {
            long targetTicks = targetDateTime.Ticks;
            string[] possibleBackups = Directory.GetFiles(Path.Combine(backupPath, "Index"));
            long closestBackup = 0;
            long closestBackupDiff = long.MaxValue;
            foreach (string possibleBackup in possibleBackups)
            {
                string possibleBackupCropped = Path.GetFileNameWithoutExtension(possibleBackup);
                if (long.TryParse(possibleBackupCropped, out long possibleBackupLong))
                {
                    long timeDiff = Math.Abs(targetTicks - possibleBackupLong);
                    if (timeDiff < closestBackupDiff)
                    {
                        closestBackup = possibleBackupLong;
                        closestBackupDiff = timeDiff;
                    }
                }
            }
            return closestBackup;
        }
    }
}
