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
        const string DEBUG_PREFIX = "DMS: ";
        const string UR_PREFIX = "UR-DMS-";
        const string AS_PREFIX = "AS-DMS-";
        const string AS_INFO_PREFIX = "AS-INFO-DMS-";
        const string BU_PREFIX = "BU-DMS-";
        const string BU_INFO_PREFIX = "BU-INFO-DMS-";

        const string TEMP_FILE_EXT = ".dms";
        const string INFO_FILE_EXT = ".dmsinfo";

        const int DEFAULT_UR_LIMIT = 8;

        //EVENTS
        public event FileSavedDelegate FileSaved;
        public event FileUpdatedDelegate FileUpdated;

        public event AutoSaveCreatedDelegate AutoSaveCreated;
        public event AutoSaveDetectedDelegate AutoSaveDetected;

        //DELEGATES
        public delegate void FileSavedDelegate(DMS<T> sender, string path);
        public delegate void FileUpdatedDelegate(ref T obj);

        public delegate void AutoSaveCreatedDelegate(ref T obj);
        public delegate void AutoSaveDetectedDelegate(DateTime time, string project, string autosave);

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
                        DebugMsg("You reached the limit of undo operations!");
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
                    DebugMsg("Correct value of Undo/Redo limit: 1-10, Default: " + DEFAULT_UR_LIMIT);
                    pLimitUndoRedo = DEFAULT_UR_LIMIT;
                }
            }
        }
        
        //VARIABLES
        T DataObject;

        Stack<UndoRedoInfo> UndoHistory = new Stack<UndoRedoInfo>();
        UndoRedoInfo CurrentCP = new UndoRedoInfo("", "");
        Stack<UndoRedoInfo> RedoHistory = new Stack<UndoRedoInfo>();

        int pLimitUndoRedo = DEFAULT_UR_LIMIT;
        Timer AutoSaveTMR = new Timer();

        //STRUCT
        private struct UndoRedoInfo
        {
            public readonly string message;
            public readonly string filename;

            public UndoRedoInfo(string message, string filename)
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
            UID = id.ToUpper();
            T t = obj ?? throw new Exception("Data object is empty!");
            DataObject = t;
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
                DebugMsg("Use \"SaveAs\" to define the save path!");
                return false;
            }
            return LoadFromFile(PathToFile, true);
        }

        private bool LoadFromFile(string path, bool originalfile = false)
        {
            if (path == "" || !File.Exists(path))
            {
                DebugMsg("File \"" + path + "\" not found!");
                return false;
            }
            T o = DeserializeObject(path);
            if (o == null) { return false; }
            DataObject = o;
            if (originalfile) { FileChanged = false; }
            DebugMsg("File \"" + path + "\" has been loaded!");
            FileUpdated?.Invoke(ref DataObject);
            return true;
        }

        #endregion

        #region "SAVING"

        public bool SaveAs(string newpath)
        {
            if (newpath == "")
            {
                DebugMsg("New path is empty!");
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
                DebugMsg("Use \"SaveAs\" to define the save path!");
                return false;
            }
            if (!FileChanged)
            {
                DebugMsg("No changes detected!");
                return false;
            }
            bool r = SerializeObject(ref DataObject, PathToFile);
            if (r)
            {
                FileChanged = false;
                DebugMsg("File \"" + PathToFile + "\" has been saved!");
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
            UndoRedoInfo f = new UndoRedoInfo(info, CreateCPFile());
            if (f.filename == "") { return false; }
            DeleteRedoHistory();
            UndoHistory.Push(f);
            CurrentCP = new UndoRedoInfo("", "");
            DebugMsg("New Check Point! File: \"" + f.filename + "\", Message: \"" + f.message + "\"");
            return true;
        }

        public void Undo()
        {
            if (!UndoRedoActivated | !UndoAvailable) { return; }
            FileChanged = true;

            if (CurrentCP.filename == "")
            {
                //save object in redo and load undo cp
                UndoRedoInfo f = new UndoRedoInfo("", CreateCPFile());
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
                DebugMsg("Undo operation completed!");
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
                DebugMsg("Redo operation completed!");
            }
        }

        public List<string> GetUndoHistory()
        {
            List<string> r = new List<string>();
            foreach(UndoRedoInfo i in UndoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        public List<string> GetRedoHistory()
        {
            List<string> r = new List<string>();
            foreach (UndoRedoInfo i in RedoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        private string CreateCPFile()
        {
            string guid = Guid.NewGuid().ToString();
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = UR_PREFIX + UID + "-" + guid + TEMP_FILE_EXT;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r) { return f; } else { return ""; }
        }

        private void DeleteCheckPoints()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates);
            string f = UR_PREFIX + UID + "*" + TEMP_FILE_EXT;
            foreach (string i in Directory.GetFiles(d, f))
            {
                try
                {
                    File.Delete(i);
                    DebugMsg("Check Point deleted! File: \"" + new FileInfo(i).Name + "\"");
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
                UndoRedoInfo f = RedoHistory.Pop();
                try
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + f.filename);
                    DebugMsg("Check Point deleted! File: \"" + f.filename + "\"");
                } catch { }
            }
        }

        #endregion

        #region "AUTOSAVE"

        public void AutoSaveService(bool activation, int Interval)
        {
            if (Interval < 1 | Interval > 30)
            {
                DebugMsg("Correct value of AutoSave Interval: 1-30. AutoSave disabled!");
                activation = false;
                Interval = 30;
            }
            AutoSaveActivated = activation;
            AutoSaveTMR.Enabled = false;
            AutoSaveTMR.Interval = Interval * 60000;
            if(activation)
            {
                AutoSaveTMR.Enabled = true;
                DebugMsg("AutoSave activated! Interval: " + Interval + " minutes");
            }
        }
        
        public bool CreateAutoSaveFile()
        {
            if (RestoreMode)
            {
                DebugMsg("AutoSave is disabled in \"Restore Mode\"!");
                return false;
            }
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = AS_PREFIX + UID + TEMP_FILE_EXT;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r)
            {
                DMSFileInfo i = new DMSFileInfo(DateTime.Now, PathToFile, d + f);
                SerializeInfo(ref i, d + AS_INFO_PREFIX + UID + INFO_FILE_EXT);
                DebugMsg("New AutoSave! File: \"" + f + "\"");
                AutoSaveCreated?.Invoke(ref DataObject);
            }
            return r;
        }
        
        public void DeleteRestoreFile()
        {
            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT))
            {
                try
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT);
                    if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_INFO_PREFIX + UID + INFO_FILE_EXT))
                    {
                        File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_INFO_PREFIX + UID + INFO_FILE_EXT);
                    }
                    RestoreMode = false;
                    DebugMsg("AutoSave file deleted!");
                } catch { }
            }
        }

        public void LoadRestoreFile()
        {
            if (RestoreMode)
            {
                try
                {
                    if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT))
                    {
                        RestoreMode = false;
                        FileChanged = true;
                        PathToFile = "";
                        DebugMsg("The data was restored successfully!");
                        DeleteRestoreFile();
                    }
                }
                catch { }
            }
            else
            {
                DebugMsg("Restore mode disabled!");
            }
        }

        public void CheckRestoreFile()
        {
            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT))
            {
                DMSFileInfo i = DeserializeInfo(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_INFO_PREFIX + UID + INFO_FILE_EXT);
                if (i == null) i = new DMSFileInfo(DateTime.Now, "", "");
                RestoreMode = true;
                DebugMsg("Data ready to recover! Use LoadRestoreFile() or DeleteRestoreFile() to choose what you want to do...");
                AutoSaveDetected?.Invoke(i.CreationTime, i.ProjectFile, i.AutoSaveFile);
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
                DebugMsg("Serialization successful! File: \"" + path + "\"");
                return true;
            }
            catch {
                DebugMsg("Serialization error! File: \"" + path + "\"");
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
                DebugMsg("Deserialization successful! File: \"" + path + "\"");
                return o;
            }
            catch {
                DebugMsg("Deserialization error! File: \"" + path + "\"");
                return null;
            }
        }

        private bool SerializeInfo(ref DMSFileInfo obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    formatter.Serialize(stream, obj);
                    stream.Close();
                }
                return true;
            } catch { return false; }
        }

        private DMSFileInfo DeserializeInfo(string path)
        {
            try
            {
                DMSFileInfo o;
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    o = (DMSFileInfo)formatter.Deserialize(stream);
                }
                return o;
            } catch { return null; }
        }

        #endregion

        #region "DEBUG"

        private void DebugMsg(string message)
        {
        #if DEBUG
            System.Diagnostics.Debug.WriteLine(DEBUG_PREFIX + message);
        #endif
        }

        #endregion

        [Serializable()]
        private class DMSFileInfo
        {
            public readonly DateTime CreationTime;
            public readonly string ProjectFile;
            public readonly string AutoSaveFile;

            public DMSFileInfo(DateTime Time, string ToProject, string ToAutoSave)
            {
                CreationTime = Time;
                ProjectFile = ToProject;
                AutoSaveFile = ToAutoSave;
            }
        }

    }
}
