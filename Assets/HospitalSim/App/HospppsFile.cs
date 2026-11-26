using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using HospitalSim.Import;
using UnityEngine;

namespace HospitalSim
{
    //nasz format pliku hospps czyli hospital politechnika slaska
    //calosc jest spakowana gzipem a w srodku po kolei
    //6 bajtow hospps int z wersja json z projektem i int z liczba siatek
    //dla kazdej siatki nazwa kolor rgb liczba floatow pozycje liczba indeksow i indeksy
    //model siedzi w srodku pliku wiec oryginalny fbx czy obj nie jest juz potem potrzebny
    public static class HospppsFile
    {
        public const string Extension = ".hospps";
        public const int FormatVersion = 1;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HOSPPS");

        public static void Save(string path, ProjectData project, RawModel model)
        {
            project.layout.Pack();
            project.formatVersion = FormatVersion;
            project.modified = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            if (string.IsNullOrEmpty(project.created)) project.created = project.modified;
            string json = JsonUtility.ToJson(project, true);
            WriteRaw(path, json, model);
        }

        public static ProjectData Load(string path, out RawModel model)
        {
            string json = ReadRaw(path, out model);
            var project = FromJson(json);
            return project;
        }

        public static ProjectData FromJson(string json)
        {
            var project = JsonUtility.FromJson<ProjectData>(json);
            if (project == null) throw new ImportException("Plik projektu jest uszkodzony.");
            if (project.layout == null) project.layout = new LayoutData();
            if (project.settings == null) project.settings = new SimSettingsBundle();
            if (project.waypoints == null) project.waypoints = new System.Collections.Generic.List<ManualWaypoint>();
            //pliki bez numeru wersji ustawien sa z poprzedniej wersji programu
            int sv = json.Contains("\"settingsVersion\"") ? project.settings.settingsVersion : 1;
            if (sv < SimSettingsBundle.CurrentVersion) project.settings.Migrate(sv);
            project.layout.Unpack();
            return project;
        }

        //zapis nie uzywa unity wiec mozna go wolac z innego watku
        //piszemy najpierw do pliku tmp zeby przerwany zapis nie zepsul projektu a poprzednia wersja zostaje jako bak
        public static void WriteRaw(string path, string json, RawModel model)
        {
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
            using (var bw = new BinaryWriter(new BufferedStream(gz, 1 << 16)))
            {
                bw.Write(Magic);
                bw.Write(FormatVersion);
                bw.Write(json);
                bw.Write(model.meshes.Count);
                foreach (var m in model.meshes)
                {
                    bw.Write(m.name ?? "");
                    bw.Write(m.r); bw.Write(m.g); bw.Write(m.b);
                    bw.Write(m.pos.Length);
                    var b = new byte[m.pos.Length * 4];
                    Buffer.BlockCopy(m.pos, 0, b, 0, b.Length);
                    bw.Write(b);
                    bw.Write(m.idx.Length);
                    b = new byte[m.idx.Length * 4];
                    Buffer.BlockCopy(m.idx, 0, b, 0, b.Length);
                    bw.Write(b);
                }
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static string ReadRaw(string path, out RawModel model)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var br = new BinaryReader(new BufferedStream(gz, 1 << 16)))
            {
                byte[] magic;
                try { magic = br.ReadBytes(6); }
                catch (Exception) { throw new ImportException("To nie jest plik .hospps."); }
                if (magic.Length != 6 || Encoding.ASCII.GetString(magic) != "HOSPPS") throw new ImportException("To nie jest plik .hospps.");
                int ver = br.ReadInt32();
                if (ver > FormatVersion) throw new ImportException("Plik zapisano w nowszej wersji programu (format " + ver + "). Zaktualizuj program.");
                string json = br.ReadString();
                int count = br.ReadInt32();
                model = new RawModel { sourceName = Path.GetFileName(path) };
                for (int i = 0; i < count; i++)
                {
                    var m = new RawMesh();
                    m.name = br.ReadString();
                    m.r = br.ReadSingle(); m.g = br.ReadSingle(); m.b = br.ReadSingle();
                    int pc = br.ReadInt32();
                    m.pos = new float[pc];
                    Buffer.BlockCopy(ReadExact(br, pc * 4), 0, m.pos, 0, pc * 4);
                    int ic = br.ReadInt32();
                    m.idx = new int[ic];
                    Buffer.BlockCopy(ReadExact(br, ic * 4), 0, m.idx, 0, ic * 4);
                    model.meshes.Add(m);
                }
                return json;
            }
        }

        static byte[] ReadExact(BinaryReader br, int n)
        {
            var b = br.ReadBytes(n);
            if (b.Length != n) throw new ImportException("Plik .hospps jest ucięty lub uszkodzony.");
            return b;
        }
    }
}
