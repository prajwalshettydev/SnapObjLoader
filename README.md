# SnapObjLoader
Fastest Way to To Load the .obj File in Unity during runtime, loads .obj files faster than Thanos' snap.

Inspired by the FastObjImporter http://wiki.unity3d.com/index.php/FastObjImporter, which only used to support blender's .obj exported file, with one mesh and one submesh.

So I improvised it so that it can be used in multiple situations or with complex and large meshes.

What the SnapObjLoader Does:

* The Whole loading happens in the Background thread so even the largest .obj files wont freeze the system while loading
* Loads obj file with any number of submeshes and meshes
* Loads Materials and textures for each submesh,
* Supports PBR Textures if available,
* Supports mesh with negative face indices
* Support Skecthup exported mesh with non numeric characters
* Supports Import time resetting of mesh's scale
* On Requirement Flips the Asset in X-axis, this Helps to stay in sync with the assets exported from major 3d modeling software like 3ds max/blender, since all of them use the right-handed coordinate system whereas unity uses the left-handed coordinate system
