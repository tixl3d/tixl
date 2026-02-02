using T3.Core.Rendering;

namespace Lib.mesh.@_;

[Guid("dd3d7e16-f33e-4fb0-89c6-4d8cbc9d702f")]
internal sealed class LoadObjEdges : Instance<LoadObjEdges>, IDescriptiveFilename
{
    [Output(Guid = "C0D0420D-84E6-4C57-8E88-D2B04DB26B89")]
    public readonly Slot<StructuredList> Data = new();

    public LoadObjEdges()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var path = Path.GetValue(context);
        if (path != _lastFilePath)
        {
            if (!TryGetFilePath(path, out var fullPath))
            {
                Log.Error($"File not found: {path}", this);
                return;
            }
                
            var mesh = ObjMesh.LoadFromFile(fullPath);
            if (mesh == null)
            {
                Log.Error($"Failed to extract edge line points from obj {path}", this);
                return;
            }
                
            var hashSet = new HashSet<uint>();

            foreach (var f in mesh.Faces)
            {
                InsertVertexPair((uint)f.V0, (uint)f.V1);
                InsertVertexPair((uint)f.V1, (uint)f.V2);
                InsertVertexPair((uint)f.V2, (uint)f.V0);
            }

            void InsertVertexPair(uint from, uint to)
            {
                if (from < to)
                {
                    (@from, to) = (to, @from);
                }

                var combined = (to << 16) + from;
                hashSet.Add(combined);
            }

            var count = hashSet.Count;
            //_pointList = new T3.Core.DataStructures.Point[count * 3];
            _pointList.SetLength(count * 3);

            var index = 0;
            foreach (var pair in hashSet)
            {
                var fromIndex = pair & 0xffff;
                var toIndex = pair >> 16;

                if (fromIndex < 0 || fromIndex > _pointList.TypedElements.Length - 1
                                  || toIndex < 0 || toIndex > _pointList.TypedElements.Length - 1)
                {
                    Log.Warning($"Skipping invalid line indices {fromIndex} / {toIndex}");
                    continue;
                }
                    
                var pFrom = mesh.Positions[(int)fromIndex];
                _pointList.TypedElements[index] = new Point()
                                                      {
                                                          Position = new Vector3(pFrom.X, pFrom.Y, pFrom.Z),
                                                          Orientation = Quaternion.Identity,
                                                          F1 = 1
                                                      };
                index++;

                var pTo = mesh.Positions[(int)toIndex];
                _pointList.TypedElements[index] = new Point()
                                                      {
                                                          Position = new Vector3(pTo.X, pTo.Y, pTo.Z),
                                                          Orientation = Quaternion.Identity,
                                                          F1 = 1
                                                      };                    
                index++;

                _pointList.TypedElements[index] = Point.Separator();
                index++;
            }

            _lastFilePath = path;
        }

        Data.Value = _pointList;
    }

    public InputSlot<string> SourcePathSlot => Path;
        

    private readonly StructuredList<Point> _pointList = new(10);

    private string _lastFilePath;

    [Input(Guid = "b6932cbd-e6b6-447b-b416-701326227864")]
    public readonly InputSlot<string> Path = new();
}