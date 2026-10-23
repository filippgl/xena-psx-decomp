using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Xena.Assets
{
    [Serializable] public sealed class PrototypePreferenceData
    {
        public int version = 1;
        public float mouseSensitivity = 0.12f;
        public bool invertY;
        public string bindingOverrides = "";
    }
    public sealed class PrototypePreferences
    {
        public PrototypePreferenceData Data { get; private set; } = new PrototypePreferenceData();
        public string Status { get; private set; } = "";
        public string FilePath { get; }
        private readonly bool disabled;
        public PrototypePreferences()
        {
            string[] args = Environment.GetCommandLineArgs();
            disabled = Array.IndexOf(args, "--no-settings") >= 0;
            int at = Array.IndexOf(args, "--settings-file");
            FilePath = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : Path.Combine(Application.persistentDataPath, "XenaPrototype.settings.json");
        }
        public void Load(InputActionAsset actions)
        {
            if (disabled || !File.Exists(FilePath)) return;
            try
            {
                var data = new PrototypePreferenceData(); JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), data);
                if (data.version != 1 || float.IsNaN(data.mouseSensitivity) || float.IsInfinity(data.mouseSensitivity) || data.mouseSensitivity < 0.03f || data.mouseSensitivity > 0.3f)
                    throw new InvalidDataException("Unsupported settings values");
                if (!string.IsNullOrEmpty(data.bindingOverrides)) actions.LoadBindingOverridesFromJson(data.bindingOverrides);
                Data = data; Status = "Settings loaded";
            }
            catch (Exception) { actions.RemoveAllBindingOverrides(); Data = new PrototypePreferenceData(); Status = "Settings load failed; defaults active. Original file kept."; }
        }
        public bool Save(InputActionAsset actions)
        {
            if (disabled) { Status = "Settings disabled for this verification"; return false; }
            try
            {
                Data.bindingOverrides = actions.SaveBindingOverridesAsJson();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temporary = FilePath + ".tmp"; File.WriteAllText(temporary, JsonUtility.ToJson(Data, true));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
                Status = "Settings saved"; return true;
            }
            catch (Exception) { Status = "Could not save settings; current session still works"; return false; }
        }
        public void Reset(InputActionAsset actions) { actions.RemoveAllBindingOverrides(); Data = new PrototypePreferenceData(); Save(actions); }
    }
}
