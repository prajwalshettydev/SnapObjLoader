/*
 * (C) 2018 prajwalshetty2018@gmail.com
 * 
 * Faster File .obj Parsing from: 
 * "Marc Kusters (Nighteyes)" (http://wiki.unity3d.com/index.php/FastObjImporter) 
 *  
 * Material and texture importer from:
 * "AARO4130" (https://assetstore.unity.com/packages/tools/modeling/runtime-obj-importer-49547)
 * 
 * DO NOT USE PARTS OF, OR THE ENTIRE SCRIPT, AND CLAIM AS YOUR OWN WORK
*/

using System;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SnapLoader
{
    public static bool splitByMaterial = false;
    public static string[] searchPaths = new string[] { "", "%FileName%_Textures" + Path.DirectorySeparatorChar };
    
    /// <summary>
    /// Should the obj flip in the x-axis, set it to -1 if yes, 
    /// Helps to stay in sync with the assets exported in makor 3d modelling softwares like 3ds max/blender,
    /// since all of them use RHR where as unity uses LHR
    /// </summary>
    private static int xAxisFlip = -1;
    
    struct OBJFace
    {
        public string materialName;
        public string meshName;
        public int[] indexes;
    }


    //functions
#if UNITY_EDITOR

    [MenuItem("GameObject/Import From OBJ new")]
    static async void ObjLoadMenuNew()
    {
        string pth = UnityEditor.EditorUtility.OpenFilePanel("Import OBJ", "", "obj");
        if (!string.IsNullOrEmpty(pth))
        {
            System.Diagnostics.Stopwatch s = new System.Diagnostics.Stopwatch();
            s.Start();
            await SnapLoadOBJ(pth, new Vector3(0, 0, 0));
            Debug.Log("OBJ load took " + s.ElapsedMilliseconds + "ms");
            s.Stop();
        }
    }
#endif

    #region OBJ Loader
    
    /* Loads Asset Before Thanos Snaps
     */ 
    public static async Task<GameObject> SnapLoadOBJ(string fn, Vector3 position)
    {
        string meshName = Path.GetFileNameWithoutExtension(fn);
        float importScale = 1; //0.0254 if inches, 0.01 if cm, 0.001 if mm,

        bool hasNormals = false;
        //OBJ LISTS
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        
        //MESH CONSTRUCTION
        List<string> materialNames = new List<string>();
        List<string> objectNames = new List<string>();
        Dictionary<string, Dictionary<string, List<Vector3Int>>> faceData = new Dictionary<string, Dictionary<string, List<Vector3Int>>>();

        string cmaterial = "";
        string cmesh = "default";

        //CACHE
        Material[] materialCache = null;
        FileInfo OBJFileInfo = new FileInfo(fn);

        Debug.Log(fn);

        foreach (string ln in File.ReadLines(fn))
        {
            /* Changing the Scale of the .obj file based on its Source
             * To convert the scale to meters
             * Might not work as intended if the imported file is not exported from known sources
             * 1 if Meteres, 0.0254 if inches, 0.01 if cm, 0.001 if mm
             */
            if (ln.Contains("3ds Max"))
            {
                Debug.LogWarning("[OBJ Importer] Exported From 3dsMax, Considering Scale as centi-meteres");
                importScale = 0.01f;
            }

            if (ln.Length > 0 && ln[0] != '#')
            {
                string l = ln.Trim().Replace("  ", " ");
                string[] cmps = l.Split(' ');
                string data = l.Remove(0, l.IndexOf(' ') + 1);

                if (cmps[0] == "mtllib")
                {
                    /* Get the File Path For the .mtl file
                     * if File Exists call the material Loader
                     */
                    string pth = OBJGetFilePath(data, OBJFileInfo.Directory.FullName + Path.DirectorySeparatorChar, meshName);
                    if (pth != null)
                        materialCache = LoadMTLFile(pth);
                    break;
                }
            }
        }

        await Task.Run(() =>
        {
            /* Read The Whole File And Copy it to a string
             * */
            string text = File.ReadAllText(fn);

            int start = 0;
            int faceDataCount = 0;

            StringBuilder sb = new StringBuilder();
            StringBuilder sbFloat = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    sb.Remove(0, sb.Length);

                    // Start +1 for whitespace '\n'
                    sb.Append(text, start + 1, i - start);
                    start = i;

                    if ((sb[0] == 'o' || sb[0] == 'g') && sb[1] == ' ')
                    {
                        sbFloat.Remove(0, sbFloat.Length);
                        int j = 2;
                        string objectName = string.Empty;
                        while (j < sb.Length && (sb[j] != '\n'))
                        {
                            objectName += sb[j];
                            j++;
                        }

                        /* Remove Any Unnecessary Universal Chars in the String
                         * "\u000D" is Carriage Return, 
                         */
                        cmesh = objectName.Replace("\u000D", "").Replace("\u000A", "");
                        if (!faceData.ContainsKey(cmesh)) faceData.Add(cmesh, new Dictionary<string, List<Vector3Int>>());

                    }
                    /* Vertices data of the Mesh
                     */
                    else if (sb[0] == 'v' && sb[1] == ' ')
                    {
                        int splitStart = 2;
                        if (sb[2] == ' ') splitStart++;

                        vertices.Add(new Vector3(GetFloat(sb, ref splitStart, ref sbFloat) * xAxisFlip,
                            GetFloat(sb, ref splitStart, ref sbFloat), GetFloat(sb, ref splitStart, ref sbFloat)) * importScale);
                        if(vertices[vertices.Count - 1].VectorApproxEquals(Vector3.zero)) { Debug.LogError("Got 000 mate at, " + vertices[vertices.Count - 1]); }
                    }
                    /* Face data of the Mesh
                     */
                    else if (sb[0] == 'f' && sb[1] == ' ')
                    {
                        int splitStart = 2, j = 1, info = 0;

                        List<Vector3Int> tfd = new List<Vector3Int>();
                        if (!faceData[cmesh].ContainsKey(cmaterial))
                            faceData[cmesh].Add(cmaterial, new List<Vector3Int>());

                        while (splitStart < sb.Length && (char.IsDigit(sb[splitStart]) || sb[splitStart] == '-'))
                        {
                            int vertexIndex = GetInt(sb, ref splitStart, ref sbFloat) - 1;

                            /* If the Vertex Index is Below Zero,
                             * Then Subtract the Index with the Size of the With current Vertex Index
                             */
                            if (vertexIndex < 0) vertexIndex = (vertices.Count - Math.Abs(vertexIndex)) + 1;

                            /* If the Current Face Data Doesnt Have the UV Info,
                             * Skip storing UVIndex 
                             */
                            int uvIndex = -1;
                            if (sb[splitStart] == '/') splitStart++;
                            else
                            {
                                uvIndex = GetInt(sb, ref splitStart, ref sbFloat) - 1;
                                if (uvIndex < 0) uvIndex = (uvs.Count - Math.Abs(uvIndex)) + 1;
                            }

                            int normIndex = GetInt(sb, ref splitStart, ref sbFloat) - 1;
                            if (normIndex < 0) normIndex = (normals.Count - Math.Abs(normIndex)) + 1;

                            tfd.Add(new Vector3Int(vertexIndex, uvIndex, normIndex));

                            j++;
                            faceDataCount++;
                        }

                        info += j;
                        j = 1;

                        if (!faceData[cmesh].ContainsKey(cmaterial)) faceData[cmesh].Add(cmaterial, new List<Vector3Int>());

                        /* Create triangles out of the face data.
                         * There will generally be more than 1 triangle per face.
                         * So Break the Face into Triangles
                         */
                        while (j + 2 < info)
                        {
                            if (xAxisFlip == -1){
                                faceData[cmesh][cmaterial].Add(tfd[j + 1]);
                                faceData[cmesh][cmaterial].Add(tfd[j]);
                                faceData[cmesh][cmaterial].Add(tfd[0]);
                            } else {
                                faceData[cmesh][cmaterial].Add(tfd[0]);
                                faceData[cmesh][cmaterial].Add(tfd[j]);
                                faceData[cmesh][cmaterial].Add(tfd[j + 1]);
                            }

                            j++;
                        }
                    }
                    else if (sb[0] == 'u' && sb[1] == 's' && sb[2] == 'e' && sb[3] == 'm')   //(sb[0] == "u0s1e2m3t4l5 67")
                    {
                        sbFloat.Remove(0, sbFloat.Length);
                        int j = 7;
                        string matName = string.Empty;
                        while (j < sb.Length && (sb[j] != '\n'))
                        {
                            matName += sb[j];
                            j++;
                        }

                        /* Remove Any Unnecessary Universal Chars in the String
                         * "\u000D" is Carriage Return, 
                         */
                        cmaterial = matName.Replace("\u000D", "").Replace("\u000A", "");
                        if (!materialNames.Contains(cmaterial))
                            materialNames.Add(cmaterial);
                    }
                    /* UV data of the Mesh
                     */
                    else if (sb[0] == 'v' && sb[1] == 't' && sb[2] == ' ')
                    {
                        int splitStart = 3;

                        uvs.Add(new Vector2(GetFloat(sb, ref splitStart, ref sbFloat),
                            GetFloat(sb, ref splitStart, ref sbFloat)));
                    }
                    /* Normal data of the Mesh
                     */
                    else if (sb[0] == 'v' && sb[1] == 'n' && sb[2] == ' ')
                    {
                        int splitStart = 3;

                        normals.Add(new Vector3(GetFloat(sb, ref splitStart, ref sbFloat),
                            GetFloat(sb, ref splitStart, ref sbFloat), GetFloat(sb, ref splitStart, ref sbFloat)));
                    }
                }
            }
        });

        GameObject parentObject = new GameObject(meshName);

        foreach (var obj in faceData)
        {
            var tCount = obj.Value.Values.Sum(x => x.Count);

            /* Skiping Empty Or unwanted Meshes
             */
            if (obj.Key.ToLower().Contains("shadow_plane") || tCount < 1) continue;

            Vector3[] newVerts = new Vector3[tCount];
            Vector2[] newUVs = new Vector2[tCount];
            Vector3[] newNormals = new Vector3[tCount];
            Dictionary<string, int[]> tris = new Dictionary<string, int[]>();
            Dictionary<string, int> remapIndices = new Dictionary<string, int>();

            /* The following foreach loops through the facedata 
             * Assigns the appropriate vertex, uv and normal for the Unity's mesh arrays.
             * Assigns Triangles to the Unity's Submesh Arrays
             */
            int i = 0;
            foreach (var mat in obj.Value)
            {
                int j = 0;
                if (!tris.ContainsKey(mat.Key)) tris.Add(mat.Key, new int[mat.Value.Count]);
                foreach (var face in mat.Value)
                {
                    /* Genearte The Hash Entry Key
                     */
                    string key = face.x + "|" + face.y + "|" + face.z;

                    /* Create New HashEntry
                     */ 
                    if (!remapIndices.ContainsKey(key))
                    {
                        newVerts[i] = vertices[face.x];
                        if (face.y >= 0) newUVs[i] = uvs[face.y];
                        if (face.z >= 0) newNormals[i] = normals[face.z];

                        remapIndices.Add(key, i);
                        i++;
                    }

                    /* Remap Traingle's one of the vertex Index,
                     * to the new index from newVert/newUV/newNormal Arrays created
                     */
                    tris[mat.Key][j] = remapIndices[key];
                    j++;

                }
            }

            /* Resize the Array to Remove Unused Array Entried.
             * Tip: Switch To Lists if memory usage is lower.
             * Resize Required because in Early stage cannot specify the exact Array size 
             * until the reorganising process is done.
             */
            Array.Resize<Vector3>(ref newVerts, i);
            Array.Resize<Vector3>(ref newNormals, i);
            Array.Resize<Vector2>(ref newUVs, i);

            GameObject subObject = new GameObject(obj.Key);
            subObject.transform.parent = parentObject.transform;

            subObject.transform.localScale = new Vector3(-1, 1, 1);

            /* Create Unity mesh object
             */ 
            Mesh m = new Mesh();
            m.name = obj.Key;

            /* Create Required Number of Submeshs
             */
            if (m.subMeshCount != obj.Value.Count)
                m.subMeshCount = obj.Value.Count;

            /* Apply UV, Verts and Normal, Before Applying Triangles
             */
            m.vertices = newVerts;
            m.normals = newNormals;
            m.uv = newUVs;

            i = 0;
            foreach (var mat in obj.Value)
            {
                m.SetTriangles(tris[mat.Key], i, true);
                i++;
            }

            /* At File Fetch Stage Check the availability of mesh's normal Data.
             * if not available let the unity calculate it here.
             */
            if (!hasNormals)
                m.RecalculateNormals();
            m.RecalculateBounds();
            
            MeshFilter mf = subObject.AddComponent<MeshFilter>();
            MeshRenderer mr = subObject.AddComponent<MeshRenderer>();

            i = 0;
            Material[] processedMaterials = new Material[obj.Value.Count];
            foreach (var mat in obj.Value)
            {
                if (materialCache == null)
                    processedMaterials[i] = new Material(Shader.Find("Diffuse"));
                else
                {
                    Material mfn = Array.Find(materialCache, x => x != null && x.name == mat.Key);
                    if (mfn == null)
                        processedMaterials[i] = new Material(Shader.Find("Diffuse"));
                    else
                        processedMaterials[i] = mfn;
                }
                processedMaterials[i].name = mat.Key;
                i++;
            }

            mf.mesh = m;
            mr.materials = processedMaterials;
        }

        parentObject.transform.position = position;
        return parentObject;
    }
    
    #endregion

    #region Textures

    /// <summary>
    /// Attempts TO Load Textures based on CurrentMaterials, 
    /// Follows Naming standards As Described in :https://docs.google.com/document/d/18KixpRTnxtHI9dkkprQjjSVu9HTqEiUPmiYMitpZVao/edit, 
    /// prajwalshetty2018@gmail.com
    /// </summary>
    /// <param name="objPath">Path To ObjFile's Directory, Will traverse through the entire tree below this path to Find Textures</param>
    /// <param name="matlList">List of materials for the mesh</param>
    /// <returns>Returns the Updated array of materials</returns>
    public static Material[] LoadTexturesToSystem(string objPath, List<Material> matlList)
    {
        var ext = new List<string> { ".jpg", ".png" };
        var myFiles = Directory.GetFiles(objPath, "*.*", SearchOption.AllDirectories)
             .Where(s => ext.Contains(Path.GetExtension(s)));

        foreach (string textureFile in myFiles)
        {
            string texFile = textureFile.ToLower();
            string[] cmps = (Path.GetFileNameWithoutExtension(texFile)).Split('_');
            //Debug.Log("Searchig for: " + cmps[0]);

            if (texFile.ContainsAny("_metalness", "_metallic", "_metal"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Mettallic map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_diffuse", "_albedo"))
            {
                Debug.LogWarning("[Texture Loader] Found a Diffuse map For mat: " + Path.GetFileNameWithoutExtension(texFile));
                foreach (Material mat in matlList)
                {
                    if (mat.name.ToLower() == cmps[0])
                    {
                        Debug.Log("[Texture Loader] Setting DiffuseTexture for: " + mat.name);
                        mat.SetTexture("_MainTex", LoadTexture(textureFile));
                        mat.SetColor("_Color", new Color(1, 1, 1));
                    }
                }
            }
            else if (texFile.ContainsAny("_specular", "_spec"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Specular map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_roughness", "_rough"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Roughness map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_ambient occlusion", "_ao", "_occlusion", "_lightmap", "_diffuseintensity"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Ambient occlusion map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_normal", "_nrm", "_normalmap"))
            {
                Debug.LogWarning("[Texture Loader] Found a Normal map For mat: " + Path.GetFileNameWithoutExtension(texFile));
                foreach (Material mat in matlList)
                {
                    string[] keys = mat.shaderKeywords;

                    Debug.Log("Keys: ");
                    for (int i = 0; i < keys.Length; i++)
                    {
                        Debug.Log("key: " + keys[i]);
                    }

                    if (mat.name.ToLower() == cmps[0])
                    {
                        Debug.Log("[Texture Loader] Setting NormalTexture for: " + mat.name);
                        mat.SetTexture("_BumpMap", LoadTexture(textureFile));
                        mat.SetFloat("_BumpScale", 0.3f);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                }
            }
            else if (texFile.ContainsAny("_bump", "_bumpmap", "_heightmap"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Bump map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_emission", "_emit", "_emissive"))
            {
                Debug.LogWarning("[Texture Loader]   Warning, Found a Emissive map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_ref", "_reflect", "_reflection"))
            {
                //_SpecGlossMap
                Debug.LogWarning("[Texture Loader] Found a Reflection map For mat: " + Path.GetFileNameWithoutExtension(texFile) + " Temp using it as Spec Map");
                foreach (Material mat in matlList)
                {
                    if (mat.name.ToLower() == cmps[0])
                    {
                        Debug.Log("[Texture Loader] Setting DiffuseTexture for: " + mat.name);
                        mat.SetTexture("_SpecGlossMap", LoadTexture(textureFile));
                        //mat.SetColor("_Color", new Color(1, 1, 1));
                    }
                }
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Reflection map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_transparency", "_transparent", "_opacity", "_mask", "_alpha"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Alpha map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
            else if (texFile.ContainsAny("_glossiness", "_glossness", "_gloss", "_glossy"))
            {
                Debug.LogWarning("[Texture Loader] InCompatibility Warning, Found a Glossiness map For mat: " + Path.GetFileNameWithoutExtension(texFile));
            }
        }

        return matlList.ToArray();
    }

    public static Texture2D LoadTGA(string fileName)
    {
        using (var imageFile = File.OpenRead(fileName))
        {
            return LoadTGA(imageFile);
        }
    }
    public static Texture2D LoadDDSManual(string ddsPath)
    {
        try
        {
            byte[] ddsBytes = File.ReadAllBytes(ddsPath);

            byte ddsSizeCheck = ddsBytes[4];
            if (ddsSizeCheck != 124)
                throw new System.Exception("Invalid DDS DXTn texture. Unable to read"); //this header byte should be 124 for DDS image files

            int height = ddsBytes[13] * 256 + ddsBytes[12];
            int width = ddsBytes[17] * 256 + ddsBytes[16];

            byte DXTType = ddsBytes[87];
            TextureFormat textureFormat = TextureFormat.DXT5;
            if (DXTType == 49)
            {
                textureFormat = TextureFormat.DXT1;
                //	Debug.Log ("DXT1");
            }

            if (DXTType == 53)
            {
                textureFormat = TextureFormat.DXT5;
                //	Debug.Log ("DXT5");
            }
            int DDS_HEADER_SIZE = 128;
            byte[] dxtBytes = new byte[ddsBytes.Length - DDS_HEADER_SIZE];
            Buffer.BlockCopy(ddsBytes, DDS_HEADER_SIZE, dxtBytes, 0, ddsBytes.Length - DDS_HEADER_SIZE);

            System.IO.FileInfo finf = new System.IO.FileInfo(ddsPath);
            Texture2D texture = new Texture2D(width, height, textureFormat, false);
            texture.LoadRawTextureData(dxtBytes);
            texture.Apply();
            texture.name = finf.Name;

            return (texture);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Error: Could not load DDS" + ex.Message);
            return new Texture2D(8, 8);
        }
    }
    public static void SetNormalMap(ref Texture2D tex)
    {
        Color[] pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color temp = pixels[i];
            temp.r = pixels[i].g;
            temp.a = pixels[i].r;
            pixels[i] = temp;
        }
        tex.SetPixels(pixels);
    }
    public static Texture2D LoadTexture(string fn, bool normalMap = false)
    {
        if (!File.Exists(fn))
            return null;
        string ext = Path.GetExtension(fn).ToLower();
        if (ext == ".png" || ext == ".jpg")
        {
            Texture2D t2d = new Texture2D(1, 1);
            t2d.LoadImage(File.ReadAllBytes(fn));
            if (normalMap)
                SetNormalMap(ref t2d);
            return t2d;
        }
        else if (ext == ".dds")
        {
            Texture2D returnTex = LoadDDSManual(fn);
            if (normalMap)
                SetNormalMap(ref returnTex);
            return returnTex;
        }
        else if (ext == ".tga")
        {
            Texture2D returnTex = LoadTGA(fn);
            if (normalMap)
                SetNormalMap(ref returnTex);
            return returnTex;
        }
        else
        {
            Debug.LogWarning("[OBJ Loader] Texture not supported : " + fn);
        }
        return null;
    }
    public static Texture2D LoadTGA(Stream TGAStream)
    {

        using (BinaryReader r = new BinaryReader(TGAStream))
        {
            /* Skip some header info we don't care about.
             * Even if we did care, we have to move the stream seek point to the beginning,
             * as the previous method in the workflow left it at the end.
             * */
            r.BaseStream.Seek(12, SeekOrigin.Begin);

            short width = r.ReadInt16();
            short height = r.ReadInt16();
            int bitDepth = r.ReadByte();

            // Skip a byte of header information we don't care about.
            r.BaseStream.Seek(1, SeekOrigin.Current);

            Texture2D tex = new Texture2D(width, height);
            Color32[] pulledColors = new Color32[width * height];

            if (bitDepth == 32)
            {
                for (int i = 0; i < width * height; i++)
                {
                    byte red = r.ReadByte();
                    byte green = r.ReadByte();
                    byte blue = r.ReadByte();
                    byte alpha = r.ReadByte();

                    pulledColors[i] = new Color32(blue, green, red, alpha);
                }
            }
            else if (bitDepth == 24)
            {
                for (int i = 0; i < width * height; i++)
                {
                    byte red = r.ReadByte();
                    byte green = r.ReadByte();
                    byte blue = r.ReadByte();

                    pulledColors[i] = new Color32(blue, green, red, 1);
                }
            }
            else
            {
                throw new Exception("TGA texture had non 32/24 bit depth.");
            }

            tex.SetPixels32(pulledColors);
            tex.Apply();
            return tex;

        }
    }

    #endregion


    #region Materials

    public static Material[] LoadMTLFile(string fn)
    {
        Debug.Log("Loading Mtl From " + fn);
        Material currentMaterial = null;
        List<Material> matlList = new List<Material>();
        FileInfo mtlFileInfo = new FileInfo(fn);
        string baseFileName = Path.GetFileNameWithoutExtension(fn);
        string mtlFileDirectory = mtlFileInfo.Directory.FullName + Path.DirectorySeparatorChar;
        foreach (string ln in File.ReadAllLines(fn))
        {
            string l = ln.Trim().Replace("  ", " ");
            string[] cmps = l.Split(' ');
            string data = l.Remove(0, l.IndexOf(' ') + 1);

            if (cmps[0] == "newmtl")
            {
                if (currentMaterial != null)
                {
                    matlList.Add(currentMaterial);
                }
                currentMaterial = new Material(Shader.Find("Standard (Specular setup)")); /*Shader.Find("Standard")*/
                currentMaterial.name = data;
            }
            else if (cmps[0].ToLower() == "kd")
            {
                currentMaterial.SetColor("_Color", ParseColorFromCMPS(cmps));
            }
            else if (cmps[0].ToLower() == "map_kd")
            {
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR
                data = data.Replace(@"\", "/");
#endif
                string fpth = OBJGetFilePath(data, mtlFileDirectory, baseFileName);
                if (fpth != null)
                {
                    Color tmpColor = new Color(1, 1, 1);
                    currentMaterial.SetTexture("_MainTex", LoadTexture(fpth));
                    currentMaterial.SetColor("_Color", tmpColor);
                }
                else
                    Debug.LogWarning("[Texture Loader] Failed To Import Texture: " + data);
            }
            else if (cmps[0] == "map_Bump")
            {
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
                data = data.Replace(@"\", "/");
#endif
                //TEXTURE
                string fpth = OBJGetFilePath(data, mtlFileDirectory, baseFileName);
                if (fpth != null)
                {
                    currentMaterial.SetTexture("_BumpMap", LoadTexture(fpth, true));
                    currentMaterial.SetFloat("_BumpScale", 0.3f);
                    currentMaterial.EnableKeyword("_NORMALMAP");
                }
                else
                    Debug.LogWarning("[Texture Loader] Failed To Import Texture: " + data);
            }
            else if (cmps[0] == "Ks")
            {
                currentMaterial.SetColor("_SpecColor", ParseColorFromCMPS(cmps));
            }
            else if (cmps[0] == "Ka")
            {
                currentMaterial.SetColor("_EmissionColor", ParseColorFromCMPS(cmps, 0.05f));
                currentMaterial.EnableKeyword("_EMISSION");
            }
            else if (cmps[0] == "d")
            {
                float visibility = float.Parse(cmps[1]);
                if (visibility < 1)
                {
                    Color temp = currentMaterial.color;

                    visibility = Mathf.Clamp(visibility, 0, 0.25f);

                    temp.a = visibility;
                    currentMaterial.shader = Shader.Find("Standard (Specular setup)");
                    currentMaterial.SetColor("_Color", temp);

                    //TRANSPARENCY ENABLER
                    currentMaterial.SetFloat("_Mode", 3);

                    //Changed By Prajwal
                    currentMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    currentMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    currentMaterial.SetInt("_ZWrite", 0);
                    currentMaterial.DisableKeyword("_ALPHATEST_ON");
                    currentMaterial.DisableKeyword("_ALPHABLEND_ON");
                    currentMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    currentMaterial.SetFloat("_Glossiness", 1f);
                    currentMaterial.renderQueue = 3000;
                }

            }
            else if (cmps[0] == "Ns")
            {
                float Ns = float.Parse(cmps[1]);
                Ns = (Ns / 100);
                currentMaterial.SetFloat("_Glossiness", Ns);
            }
        }
        if (currentMaterial != null)
        {
            matlList.Add(currentMaterial);
        }
        return LoadTexturesToSystem(mtlFileDirectory, matlList);
    }

    #endregion


    #region Static Utils

    public static Color ParseColorFromCMPS(string[] cmps, float scalar = 1.0f)
    {
        float Kr = float.Parse(cmps[1]) * scalar;
        float Kg = float.Parse(cmps[2]) * scalar;
        float Kb = float.Parse(cmps[3]) * scalar;
        return new Color(Kr, Kg, Kb);
    }

    public static string OBJGetFilePath(string path, string basePath, string fileName)
    {
        foreach (string sp in searchPaths)
        {
            string s = sp.Replace("%FileName%", fileName);
            if (File.Exists(basePath + s + path))
            {
                //Debug.Log()
                return basePath + s + path;
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static void FlipXAxis(ref List<Vector3> vs)
    {
        for (int i = 0; i < vs.Count; i++)
        {
            Vector3 v = vs[i];
            v.x *= -1;
            vs[i] = v;
        }
    }
    
    
    private static List<int> triangles;
    private static List<Vector3> vertices;
    private static List<Vector2> uv;
    private static List<Vector3> normals;
    private static List<Vector3Int> faceData;
    private static List<int> intArray;

    private const int MIN_POW_10 = -16;
    private const int MAX_POW_10 = 16;
    private const int NUM_POWS_10 = MAX_POW_10 - MIN_POW_10 + 1;
    private static readonly float[] pow10 = GenerateLookupTable();

    private static float GetFloat(StringBuilder sb, ref int start, ref StringBuilder sbFloat)
    {
        sbFloat.Remove(0, sbFloat.Length);
        while (start < sb.Length &&
               (char.IsDigit(sb[start]) || sb[start] == '-' || sb[start] == '.'))
        {
            sbFloat.Append(sb[start]);
            start++;
        }
        if (sb[start] == 'e' && sb[start + 1] == '-')
        {
            sbFloat.Remove(0, sbFloat.Length);
            sbFloat.Append(0);
            start += 2;
            float multi = float.Parse(sb[start].ToString() + sb[start + 1]);
            start += 3;
            return ParseFloat(sbFloat) * -multi;
        }
        start++;
        return ParseFloat(sbFloat);
    }

    private static int GetInt(StringBuilder sb, ref int start, ref StringBuilder sbInt)
    {
        sbInt.Remove(0, sbInt.Length);
        int multiplyer = 1;

        if (sb[start] == '-') { multiplyer = -1; start++; }

        while (start < sb.Length &&
               (char.IsDigit(sb[start])))
        {
            sbInt.Append(sb[start]);
            start++;
        }
        start++;

        return IntParseFast(sbInt) * multiplyer;
    }
    
    private static float[] GenerateLookupTable()
    {
        var result = new float[(-MIN_POW_10 + MAX_POW_10) * 10];
        for (int i = 0; i < result.Length; i++)
            result[i] = (float)((i / NUM_POWS_10) *
                    Mathf.Pow(10, i % NUM_POWS_10 + MIN_POW_10));
        return result;
    }

    private static float ParseFloat(StringBuilder value)
    {
        float result = 0;
        bool negate = false;
        int len = value.Length;
        int decimalIndex = value.Length;
        for (int i = len - 1; i >= 0; i--)
            if (value[i] == '.')
            { decimalIndex = i; break; }
        int offset = -MIN_POW_10 + decimalIndex;
        for (int i = 0; i < decimalIndex; i++)
            if (i != decimalIndex && value[i] != '-')
                result += pow10[(value[i] - '0') * NUM_POWS_10 + offset - i - 1];
            else if (value[i] == '-')
                negate = true;
        for (int i = decimalIndex + 1; i < len; i++)
            if (i != decimalIndex)
                result += pow10[(value[i] - '0') * NUM_POWS_10 + offset - i];
        if (negate)
            result = -result;
        return result;
    }

    private static int IntParseFast(StringBuilder value)
    {
        // An optimized int parse method.
        int result = 0;
        for (int i = 0; i < value.Length; i++)
        {
            result = 10 * result + (value[i] - 48);
        }
        return result;
    }
    
    #endregion
}

public sealed class Vector3Int
{
    public int x { get; set; }
    public int y { get; set; }
    public int z { get; set; }

    public Vector3Int() { }

    public Vector3Int(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
}

public static class Temp
{
    /// <summary>
    /// Checks whetherthe string has any of the input params
    /// </summary>
    /// <param name="haystack"></param>
    /// <param name="needles"></param>
    /// <returns></returns>
    public static bool ContainsAny(this string haystack, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks Whether a vector3 is approximetly equal to the seond vector3, helpfull when you have errors due to floating point precision
    /// </summary>
    /// <param name="LHS"></param>
    /// <param name="RHS"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static bool VectorApproxEquals(this Vector3 LHS, Vector3 RHS, float threshold = 0.03f)
    {
        if (Math.Abs(LHS.x - RHS.x) < threshold && Math.Abs(LHS.y - RHS.y) < threshold && Math.Abs(LHS.y - RHS.y) < threshold)
            return true;
        return false;
    }
}
