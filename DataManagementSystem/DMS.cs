using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace DataManagementSystem
{
    public class DMS<T> where T : class
    {
        //CONST
        const string debugprefix = "DMS: ";

        //EVENTS
        public delegate void FileSavedDelegate(DMS<T> sender, string path);
        public event FileSavedDelegate FileSaved;
        public delegate void FileUpdatedDelegate(ref T obj);
        public event FileUpdatedDelegate FileUpdated;

        //READONLY PROPERTIES
        public WorkingMode Mode { get; private set; }
        public string PathToFile { get; private set; }
        public bool FileChanged { get; private set; }

        //VARIABLES
        T DataObject;

        //ENUMS
        public enum WorkingMode
        {
            StaticFile = 0,
            ProjectFile = 1
        }

        public DMS(ref T obj, WorkingMode mode, string file)
        {
            DataObject = obj;
            Mode = mode;
            PathToFile = file;
            FileChanged = false;
        }

        public static string GetInfo()
        {
            return "This library was created by adan2013. More info and full documentation you can find here: https://github.com/adan2013/DataManagementSystem";
        }

        #region "RECOVERY"

        public bool LoadFromSource()
        {
            return LoadFromFile(PathToFile);
        }

        private bool LoadFromFile(string path)
        {
            if (PathToFile == "" || !File.Exists(PathToFile)) { return false; }
            T o = DeserializeObject(PathToFile);
            if (o == null) { return false; }
            DataObject = o;
            FileUpdated?.Invoke(ref DataObject);
            return true;
        }

        #endregion

        #region "SAVING"

        public bool SaveAs(string newpath)
        {
            if(Mode == WorkingMode.StaticFile)
            {
                System.Diagnostics.Debug.WriteLine(debugprefix + "SaveAs it's only available in ProjectFile mode!");
                return false;
            }
            PathToFile = newpath;
            FileChanged = true;
            return SaveChanges();
        }

        public bool SaveChanges()
        {
            if(!FileChanged)
            {
                System.Diagnostics.Debug.WriteLine(debugprefix + "No changes detected!");
                return false;
            }
            bool r = SerializeObject(DataObject, PathToFile);
            if(r)
            {
                FileChanged = false;
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

        public void AddCheckPoint(string info = "")
        {
            //TODO add checkpoint
        }

        public void UndoChanges()
        {
            //TODO undo
        }

        public void RedoChanges()
        {
            //TODO redo
        }

        public void DeleteHistoryOfChanges()
        {
            //TODO delete history of changes
        }

        #endregion

        #region "AUTOSAVE"

        public void CreateAutosave()
        {
            //TODO create autosave
        }

        public void CancelAutosave()
        {
            //TODO cancel autosave
        }

        public int GetTimeLastAutosave()
        {
            //TODO last time autosave
            return 0;
        }

        #endregion

        #region "SERIALIZATION"

        private bool SerializeObject(T obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write);
                formatter.Serialize(stream, obj);
                stream.Close();
                return true;
            }
            catch { return false;  }
        }

        private T DeserializeObject(string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                return (T)formatter.Deserialize(stream);
            }
            catch { return null; }
        }

        #endregion

    }
}
