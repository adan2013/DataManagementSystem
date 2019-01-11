using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Timers;

namespace DataManagementSystem
{
    public class DMS<T> where T : class
    {
        //CONST
        const string DebugPrefix = "DMS: ";
        const string UR_Prefix = "UR-DMS";
        const string AS_Prefix = "AS-DMS";
        const string BU_Prefix = "BU-DMS";

        const string TempFileExtension = ".dms";
        const string InfoFileExtension = ".dmsinfo";

        const int DefaultLimitUndoRedo = 8;

        //EVENTS
        public event FileSavedDelegate FileSaved;
        public event FileUpdatedDelegate FileUpdated;

        public event AutoSaveCreatedDelegate AutoSaveCreated;
        public event AutoSaveDetectedDelegate AutoSaveDetected;

        //DELEGATES
        public delegate void FileSavedDelegate(DMS<T> sender, string path);
        public delegate void FileUpdatedDelegate(ref T obj);

        public delegate void AutoSaveCreatedDelegate(ref T obj);
        public delegate void AutoSaveDetectedDelegate();

        //READONLY PROPERTIES
        public string UID { get; private set; }
        public string PathToFile { get; private set; }
        public bool FileChanged { get; private set; }

        public bool UndoRedoActivated { get; private set; }
        public bool AutoSaveActivated { get; private set; }
        public bool BackUpActivated { get; private set; }

        public bool UndoAvailable
        {
            get
            {
                if (UndoHistory.Count > 0 & UndoRedoActivated)
                {
                    if (RedoHistory.Count >= pLimitUndoRedo)
                    {
                        DebugLog("You reached the limit of undo operations!");
                        return false;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            set { }
        }
        public bool RedoAvailable
        {
            get
            {
                if (RedoHistory.Count > 0 & UndoRedoActivated) { return true; } else { return false; }
            }
            set { }
        }

        public bool RestoreMode { get; private set; }

        //PROPERTIES
        public int LimitUndoRedo
        {
            get
            {
                return pLimitUndoRedo;
            }
            private set
            {
                if (value > 0 & value < 11)
                {
                    pLimitUndoRedo = value;
                }
                else
                {
                    DebugLog("Correct value of Undo/Redo limit: 1-10, Default: " + DefaultLimitUndoRedo);
                }
            }
        }
        
        //VARIABLES
        T DataObject;

        Stack<UndoRedoHistory> UndoHistory = new Stack<UndoRedoHistory>();
        UndoRedoHistory CurrentCP = new UndoRedoHistory("", "");
        Stack<UndoRedoHistory> RedoHistory = new Stack<UndoRedoHistory>();

        int pLimitUndoRedo = DefaultLimitUndoRedo;
        Timer AutoSaveTMR = new Timer();

        //STRUCT
        private struct UndoRedoHistory
        {
            public readonly string message;
            public readonly string filename;

            public UndoRedoHistory(string message, string filename)
            {
                this.message = message;
                this.filename = filename;
            }
        }

        public DMS(string id, ref T obj, string file)
        {
            if (!ValidateID(id))
            {
                throw new Exception("Incorrect ID value! Check documentation for more information!");
            }
            if (obj == null)
            {
                throw new Exception("Data object is empty!");
            }
            UID = id.ToUpper();
            DataObject = obj;
            PathToFile = file;
            FileChanged = false;
            //UNDOREDO
            DeleteCheckPoints();
            //AUTOSAVE
            AutoSaveTMR.Elapsed += TimerTick;
        }

        private bool ValidateID(string input)
        {
            if (input.Length == 0 | input.Length > 10) { return false; }
            input = input.ToUpper();
            for (int i = 0; i < input.Length; i++)
            {
                if ("1234567890QWERTYUIOPASDFGHJKLZXCVBNM".IndexOf(input.Substring(i, 1)) < 0) { return false; }
            }
            return true;
        }

        public static string GetInfo()
        {
            return "This library was created by adan2013. More info and full documentation you can find here: https://github.com/adan2013/DataManagementSystem";
        }

        #region "RECOVERY"

        public bool LoadFromSource()
        {
            if (PathToFile == "")
            {
                DebugLog("Use \"SaveAs\" to define the save path!");
                return false;
            }
            return LoadFromFile(PathToFile, true);
        }

        private bool LoadFromFile(string path, bool originalfile = false)
        {
            if (path == "" || !File.Exists(path))
            {
                DebugLog("File \"" + path + "\" not found!");
                return false;
            }
            T o = DeserializeObject(path);
            if (o == null) { return false; }
            DataObject = o;
            if (originalfile) { FileChanged = false; }
            DebugLog("File \"" + path + "\" has been loaded!");
            FileUpdated?.Invoke(ref DataObject);
            return true;
        }

        #endregion

        #region "SAVING"

        public bool SaveAs(string newpath)
        {
            if (newpath == "")
            {
                DebugLog("New path is empty!");
                return false;
            }
            PathToFile = newpath;
            FileChanged = true;
            return SaveChanges();
        }

        public bool SaveChanges()
        {
            if (PathToFile == "")
            {
                DebugLog("Use \"SaveAs\" to define the save path!");
                return false;
            }
            if (!FileChanged)
            {
                DebugLog("No changes detected!");
                return false;
            }
            bool r = SerializeObject(ref DataObject, PathToFile);
            if (r)
            {
                FileChanged = false;
                DebugLog("File \"" + PathToFile + "\" has been saved!");
                FileSaved?.Invoke(this, PathToFile);
                return true;
            }
            return false;
        }

        public void CheckChanges()
        {
            FileChanged = true;
        }

        #endregion

        #region "UNDOREDO"

        public void UndoRedoService(bool activation, int limit)
        {
            UndoRedoActivated = activation;
            DeleteUndoHistory();
            DeleteRedoHistory();
            pLimitUndoRedo = limit;
            DeleteCheckPoints();
        }

        public bool AddCheckPoint(string info = "")
        {
            if (!UndoRedoActivated) { return false; }
            FileChanged = true;
            UndoRedoHistory f = new UndoRedoHistory(info, CreateCPFile());
            if (f.filename == "") { return false; }
            DeleteRedoHistory();
            UndoHistory.Push(f);
            CurrentCP = new UndoRedoHistory("", "");
            DebugLog("New Check Point! File: \"" + f.filename + "\", Message: \"" + f.message + "\"");
            return true;
        }

        public void Undo()
        {
            if (!UndoRedoActivated | !UndoAvailable) { return; }
            FileChanged = true;

            if (CurrentCP.filename == "")
            {
                //save object in redo and load undo cp
                UndoRedoHistory f = new UndoRedoHistory("", CreateCPFile());
                if (f.filename == "") { return; }
                RedoHistory.Push(f);
            }
            else
            {
                //save current cp in redo and load undo cp
                RedoHistory.Push(CurrentCP);
            }
            CurrentCP = UndoHistory.Pop();
            if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + CurrentCP.filename))
            {
                DebugLog("Undo operation completed!");
            }
        }

        public void Redo()
        {
            if (!UndoRedoActivated | !RedoAvailable) { return; }
            FileChanged = true;
 
            UndoHistory.Push(CurrentCP);
            CurrentCP = RedoHistory.Pop();
            if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + CurrentCP.filename))
            {
                DebugLog("Redo operation completed!");
            }
        }

        public List<string> GetUndoHistory()
        {
            List<string> r = new List<string>();
            foreach(UndoRedoHistory i in UndoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        public List<string> GetRedoHistory()
        {
            List<string> r = new List<string>();
            foreach (UndoRedoHistory i in RedoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        private string CreateCPFile()
        {
            string guid = Guid.NewGuid().ToString();
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = UR_Prefix + "-" + UID + "-" + guid + TempFileExtension;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r) { return f; } else { return ""; }
        }

        private void DeleteCheckPoints()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates);
            string f = UR_Prefix + "-" + UID + "*" + TempFileExtension;
            foreach (string i in Directory.GetFiles(d, f))
            {
                try
                {
                    File.Delete(i);
                    DebugLog("Check Point deleted! File: \"" + new FileInfo(i).Name + "\"");
                } catch { }
            }
        }

        private void DeleteUndoHistory()
        {
            UndoHistory.Clear();
        }

        private void DeleteRedoHistory()
        {
            while (RedoHistory.Count > 0)
            {
                UndoRedoHistory f = RedoHistory.Pop();
                try
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + f.filename);
                    DebugLog("Check Point deleted! File: \"" + f.filename + "\"");
                } catch { }
            }
        }

        #endregion

        #region "AUTOSAVE"

        public void AutoSaveService(bool activation, int Interval)
        {
            if (Interval < 1 | Interval > 30)
            {
                DebugLog("Correct value of Undo/Redo limit: 1-30. AutoSave disabled!");
                return;
            }
            AutoSaveActivated = activation;
            AutoSaveTMR.Enabled = false;
            AutoSaveTMR.Interval = Interval * 60000;
            if(activation)
            {
                AutoSaveTMR.Enabled = true;
                DebugLog("AutoSave activated! Interval: " + Interval + " minutes");
            }
        }
        
        public bool CreateAutoSaveFile()
        {
            if (RestoreMode)
            {
                DebugLog("AutoSave is disabled in \"Restore Mode\"!");
                return false;
            }
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = AS_Prefix + "-" + UID + TempFileExtension;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r)
            {
                DebugLog("New AutoSave! File: \"" + f + "\"");
                AutoSaveCreated?.Invoke(ref DataObject);
            }
            return r;
        }
        
        public void DeleteRestoreFile()
        {
            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_Prefix + "-" + UID + TempFileExtension))
            {
                try
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_Prefix + "-" + UID + TempFileExtension);
                    RestoreMode = false;
                    DebugLog("AutoSave file deleted!");
                } catch { }
            }
        }

        public void LoadRestoreFile()
        {
            if (RestoreMode)
            {
                try
                {
                    if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_Prefix + "-" + UID + TempFileExtension))
                    {
                        RestoreMode = false;
                        FileChanged = true;
                        PathToFile = "";
                        DebugLog("The data was restored successfully!");
                        DeleteRestoreFile();
                    }
                }
                catch { }
            }
            else
            {
                DebugLog("Restore mode disabled!");
            }
        }

        public void CheckRestoreFile()
        {
            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_Prefix + "-" + UID + TempFileExtension))
            {
                RestoreMode = true;
                DebugLog("Data ready to recover! Use LoadRestoreFile() or DeleteRestoreFile() to choose what you want to do...");
                AutoSaveDetected?.Invoke();
            }
        }

        private void TimerTick(Object source, ElapsedEventArgs e)
        {
            CreateAutoSaveFile();
        }
        
        #endregion

        #region "SERIALIZATION"

        private bool SerializeObject(ref T obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    formatter.Serialize(stream, obj);
                    stream.Close();
                }
                DebugLog("Serialization successful! File: \"" + path + "\"");
                return true;
            }
            catch {
                DebugLog("Serialization error! File: \"" + path + "\"");
                return false;
            }
        }

        private T DeserializeObject(string path)
        {
            try
            {
                T o;
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    o = (T)formatter.Deserialize(stream);
                }
                DebugLog("Deserialization successful! File: \"" + path + "\"");
                return o;
            }
            catch {
                DebugLog("Deserialization error! File: \"" + path + "\"");
                return null;
            }
        }

        #endregion

        #region "DEBUG"
        
        private void DebugLog(string message)
        {
        #if DEBUG
            System.Diagnostics.Debug.WriteLine(DebugPrefix + message);
        #endif
        }

        #endregion

    }
}
