using System.Xml.Serialization;

namespace Operators.Utils;

public static class HitFilmComposite
{
    public sealed class Vec3
    {
        [XmlAttribute("X")]
        public double X { get; set; }
        
        [XmlAttribute("Y")]
        public double Y { get; set; }
        
        [XmlAttribute("Z")]
        public double Z { get; set; }
    }
    
    public sealed class Keyframe
    {
        [XmlAttribute("Time")]
        public double Time { get; set; }
        
        public Vec3 Position { get; set; }
    }
    
    public sealed class Layer
    {
        [XmlAttribute("type")]
        public string Type { get; set; }
        
        [XmlElement("Keyframe")]
        public List<Keyframe> Keyframes { get; set; }
    }
    
    [XmlRoot("BiffCompositeShot")]
    public sealed class BiffCompositeShot
    {
        public CompositionAsset CompositionAsset { get; set; }
    }
    
    public sealed class CompositionAsset
    {
        [XmlAttribute("Version")]
        public int Version { get; set; }
        
        public Layers Layers { get; set; }
    }
    
    public sealed class Layers
    {
        public CameraLayer CameraLayer { get; set; }
    }
    
    public sealed class CameraLayer
    {
        [XmlAttribute("Version")]
        public int Version { get; set; }
        
        public LayerBase LayerBase { get; set; }
    }
    
    public sealed class LayerBase
    {
        [XmlAttribute("Version")]
        public int Version { get; set; }
        
        public string ID { get; set; }
        
        public string Name { get; set; }
        
        public string CompID { get; set; }
        
        public string ParentLayerID { get; set; }
        
        public int StartFrame { get; set; }
        
        public int EndFrame { get; set; }
        
        public int BlendMode { get; set; }
        
        public int Visible { get; set; }
        
        public int Muted { get; set; }
        
        public int Locked { get; set; }
        
        public int MotionBlurOn { get; set; }
        
        [XmlElement("PropertyManager")]
        public PropertyManager PropertyManager { get; set; }
    }
    
    public sealed class PropertyManager
    {
        [XmlElement("position")]
        public Vec3Property Position { get; set; }
        
        [XmlElement("target")]
        public Vec3Property Target { get; set; }
        
        [XmlElement("orientation")]
        public Vec3Property Orientation { get; set; }
        
        // [XmlElement("aperture")]
        // public ScalarProperty Aperture { get; set; }
    }
    
    
    public sealed class Vec3Property
    {
        [XmlAttribute("Type")]
        public int Type { get; set; }
        
        [XmlAttribute("Spatial")]
        public int Spatial { get; set; }
        
        [XmlElement("Default")]
        public Vec3b Default { get; set; }
        
        [XmlElement("Static")]
        public Vec3b Static { get; set; }
        
        [XmlElement("Animation")]
        public Animation Animation { get; set; }
    }
    
    
    public sealed class Vec3b
    {
        [XmlElement("FXPoint3_32f")]
        public FXPoint3 Point { get; set; }
    }
    
    public sealed class ScalarProperty
    {
        [XmlAttribute("Type")]
        public int Type { get; set; }
        
        [XmlElement("Default")]
        public float Default { get; set; }
        
        [XmlElement("Static")]
        public float Static { get; set; }
        
        [XmlElement("Animation")]
        public ScalarAnimation Animation { get; set; }
    }
    
    public sealed class FXPoint3
    {
        [XmlAttribute("X")]
        public float X { get; set; }
        
        [XmlAttribute("Y")]
        public float Y { get; set; }
        
        [XmlAttribute("Z")]
        public float Z { get; set; }
    }
    
    public sealed class Animation
    {
        [XmlElement("Key")]
        public List<Vec3Key> Keys { get; set; }
    }
    
    public sealed class ScalarAnimation
    {
        [XmlElement("Key")]
        public List<ScalarKey> Keys { get; set; }
    }
    
    public sealed class Vec3Key
    {
        [XmlAttribute("Type")]
        public int Type { get; set; }
        
        [XmlAttribute("Time")]
        public double Time { get; set; }
        
        [XmlElement("Value")]
        public Vec3Value Value { get; set; }
    }
    
    public sealed class Vec3Value
    {
        [XmlElement("FXPoint3_32f")]
        public FXPoint3 Point { get; set; }
        
        [XmlElement("Orientation3D")]
        public FXPoint3 Orientation { get; set; }
    }
    
    public sealed class ScalarKey
    {
        [XmlAttribute("Type")]
        public int Type { get; set; }
        
        [XmlAttribute("Time")]
        public double Time { get; set; }
        
        [XmlElement("Value")]
        public float Value { get; set; }
    }
    
    public sealed class TransformationKey
    {
        public double TimeInSeconds;
        public Vector3 Position;
        public Vector3 Target;
        public Vector3 Orientation;
    }
    
    public static class HitFilm
    {
        public static bool Load(string filePath, out List<TransformationKey> orderedKeys)
        {
            orderedKeys = new List<TransformationKey>();
            
            if (!File.Exists(filePath))
            {
                Log.Warning($"Can't find {filePath}");
                return false;
            }
            
            var keys = new Dictionary<double, TransformationKey>();
            
            var timeResolution = 1.0 / 1000;
            
            var serializer = new XmlSerializer(typeof(BiffCompositeShot));
            using var reader = new StreamReader(filePath);
            
            BiffCompositeShot compositeShot;
            try
            {
                compositeShot = (BiffCompositeShot)serializer.Deserialize(reader);
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to load {filePath}: {e.Message}");
                return false;
            }
            
            var pm = compositeShot?.CompositionAsset?.Layers?.CameraLayer?.LayerBase?.PropertyManager;
            if (pm == null)
            {
                Log.Warning("No PropertyManager found in CameraLayer");
                return false;
            }
            
            
            foreach(var key in pm.Position.Animation.Keys)
            {
                if (!keys.TryGetValue(key.Time, out var transformKey))
                {
                    transformKey = new TransformationKey { TimeInSeconds = key.Time * timeResolution };
                    keys[key.Time] = transformKey;
                }
                
                transformKey.Position = new Vector3(key.Value.Point.X, key.Value.Point.Y, key.Value.Point.Z);
                transformKey.Target = Vector3.Zero;
            }
            
            foreach(var key in pm.Orientation.Animation.Keys)
            {
                if (!keys.TryGetValue(key.Time, out var transformKey))
                {
                    transformKey = new TransformationKey { TimeInSeconds = key.Time * timeResolution };
                    keys[key.Time] = transformKey;
                }
                
                // NOTE the order Y, X, Z is correct for HitFilm 
                transformKey.Orientation = new Vector3(key.Value.Orientation.Y, key.Value.Orientation.X, key.Value.Orientation.Z);
            }
            
            orderedKeys.AddRange(keys.Values.OrderBy(k => k.TimeInSeconds));
            return true;
        }
    }
}