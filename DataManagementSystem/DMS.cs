using System;

namespace DataManagementSystem
{
    public class DMS
    {
        //EVENTS
        public delegate void FileSavedDelegate(DMS sender, string path);
        public event FileSavedDelegate FileSaved;

        //READONLY PROPERTIES
        public WorkingMode Mode { get; private set; }
        public string PathToFile { get; private set; }
        public bool FileChanged { get; private set; }

        //ENUMS
        public enum WorkingMode
        {
            StaticFile = 0,
            ProjectFile = 1
        }

        public DMS(WorkingMode mode, string file)
        {
            Mode = mode;
            PathToFile = file;
            FileChanged = false;
        }

        public static string GetInfo()
        {
            return "This library was created by adan2013. More info and full documentation you can find here: https://github.com/adan2013/DataManagementSystem";
        }

        #region "SAVING"

        public bool SaveAs(string newpath, bool DeleteExistFile)
        {
            //TODO save as
            if(System.IO.File.Exists(newpath))
            {
                if(DeleteExistFile)
                {
                    try
                    {
                        System.IO.File.Delete(newpath);
                    }
                    catch { return false; }
                }
                else { return false; }
            }
            PathToFile = newpath;
            SaveChanges();
            return true;
        }

        public void SaveChanges()
        {
            //TODO save changes
            FileSaved(this, PathToFile);
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
    }
}
