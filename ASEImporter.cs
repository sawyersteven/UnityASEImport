using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Text;
using System.Collections.Generic;

public class ASEImporter : Editor
{

    private enum BlockType
    {
        Color = 0x0001,
        GroupStart = 0xc001,
        GroupEnd = 0xc002
    }

    private struct Block
    {
        public ushort Type;
        public int Length;
    }

    private struct ColorData
    {

        public ushort NameLen { get; private set; }
        public string Name { get; private set; }
        public string ColorSpace { get; private set; }
        public float[] Values { get; private set; }
        public string Type { get; private set; }

        private string repr;

        public string ToYamlFormat()
        {
            if (repr == null)
            {
                float r = 0, g = 0, b = 0, a = 1;

                switch (ColorSpace)
                {
                    case "CMYK":
                        r = 255 * (1 - Values[0]) * (1 - Values[3]);
                        g = 255 * (1 - Values[1]) * (1 - Values[3]);
                        b = 255 * (1 - Values[2]) * (1 - Values[3]);
                        break;
                    case "LAB ":
                        throw new System.Exception("LAB conversion not yet supported");
                    case "RGB ":
                        r = Values[0];
                        g = Values[1];
                        b = Values[2];
                        break;
                    case "GRAY":
                        r = g = b = Values[0];
                        break;
                }
                repr = $"{{r: {r}, g: {g}, b: {b}, a: {a}}}";
            }
            return repr;
        }

        public void ReadFromStream(BinaryReader reader)
        {
            NameLen = BigEndian(reader.ReadUInt16());
            if (NameLen > 0)
            {
                Name = Encoding.BigEndianUnicode.GetString(reader.ReadBytes(NameLen * 2)).Trim('\0');
            }
            ColorSpace = Encoding.ASCII.GetString(reader.ReadBytes(4));
            switch (ColorSpace)
            {
                case "CMYK":
                    Values = new float[4];
                    break;
                case "RGB ":
                case "LAB ":
                    Values = new float[3];
                    break;
                case "GRAY":
                    Values = new float[1];
                    break;
                default:
                    throw new System.Exception($"Invalid colorspace: {ColorSpace}");
            }

            for (int i = 0; i < Values.Length; i++)
            {
                Values[i] = BigEndian(reader.ReadSingle());
            }

            ushort type = BigEndian(reader.ReadUInt16());
            switch (type)
            {
                case 0:
                    Type = "Global";
                    break;
                case 1:
                    Type = "Spot";
                    break;
                case 2:
                    Type = "Normal";
                    break;
                default:
                    throw new System.Exception($"Invalid color type {type}");
            }
        }

    }

    [MenuItem("Assets/Import ASE Palette")]
    private static void ImportASE()
    {
        string path = EditorUtility.OpenFilePanel("Import ase Palette", "", "ase");

        using (FileStream fileStream = File.OpenRead(path))
        using (BinaryReader binaryReader = new BinaryReader(fileStream))
        {

            if (fileStream.Length < 12)
            {
                ReportError(new Exception("ASE too short to contain required information"));
                return;
            }

            if (Encoding.ASCII.GetString(binaryReader.ReadBytes(4)) != "ASEF")
            {
                ReportError(new Exception("Invalid file header"));
                return;
            }

            ushort majVer = BigEndian(binaryReader.ReadUInt16());
            ushort minVer = BigEndian(binaryReader.ReadUInt16());

            if (majVer != 1 || minVer != 0)
            {
                ReportError(new Exception("Invalid file version"));
                return;
            }

            int blockCount = BigEndian(binaryReader.ReadInt32());

            List<ColorData> colors = new List<ColorData>();

            for (int i = 0; i < blockCount; i++)
            {
                Block block = new Block();
                try
                {
                    block.Type = BigEndian(binaryReader.ReadUInt16());
                    block.Length = BigEndian(binaryReader.ReadInt32());
                    switch (block.Type)
                    {
                        case (ushort)BlockType.Color:
                            ColorData color = new ColorData();
                            try
                            {
                                color.ReadFromStream(binaryReader);
                            }
                            catch (Exception e)
                            {
                                ReportError(e);
                                return;
                            }
                            colors.Add(color);
                            break;
                        case (ushort)BlockType.GroupStart:
                            // Don't care about groups so we just need to measure and skip this data
                            uint nameLen = BigEndian(binaryReader.ReadUInt16());
                            if (nameLen > 0)
                            {
                                binaryReader.BaseStream.Seek(nameLen, SeekOrigin.Current);
                            }
                            break;
                        case (ushort)BlockType.GroupEnd:
                            break;
                        default:
                            throw new System.Exception($"Invalid block type {block.Type}");
                    }
                }
                catch (Exception e)
                {
                    ReportError(e);
                    return;
                }
            }

            string outPath = Path.Combine(Application.dataPath, "Editor", Path.GetFileNameWithoutExtension(path) + ".colors");

            AssetDatabase.StartAssetEditing();
            System.IO.File.WriteAllText(outPath, CreateYaml(colors));
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();

        }
    }

    private static string CreateYaml(List<ColorData> colors)
    {
        string yaml = fileTemplate;
        foreach (ColorData c in colors)
        {
            yaml += $@"  - m_Name: '{c.Name}'
    m_Color: {c.ToYamlFormat()}
";
        }
        return yaml;
    }

    private static void ReportError(Exception e)
    {
        Debug.LogException(e);
        EditorUtility.DisplayDialog("ASE Decode Error", e.Message, "OK");
    }

    #region BigEndian converters
    private static int BigEndian(int value)
    {
        if (!BitConverter.IsLittleEndian) return value;
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static ushort BigEndian(ushort value)
    {
        if (!BitConverter.IsLittleEndian) return value;
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return BitConverter.ToUInt16(bytes, 0);
    }

    private static float BigEndian(float value)
    {
        if (!BitConverter.IsLittleEndian) return value;
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }
    #endregion

    private const string fileTemplate = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 52
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 12323, guid: 0000000000000000e000000000000000, type: 0}
  m_Name: 
  m_EditorClassIdentifier: 
  m_Presets:
";

}