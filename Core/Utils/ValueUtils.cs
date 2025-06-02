using System;
using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;
using Int3 = T3.Core.DataTypes.Vector.Int3;

// ReSharper disable RedundantNameQualifier

namespace T3.Core.Utils;

public static class ValueUtils
{
    /// <summary>
    /// Defines which values types can be blended and thus be part of presets and snapshots.
    /// </summary>
    public static readonly Dictionary<Type, Func<InputValue, InputValue, float, InputValue>> BlendMethods =
        new()
            {
                {
                    typeof(float), (a, b, t) =>
                                   {
                                       if (a is not InputValue<float> aValue || b is not InputValue<float> bValue)
                                           return null;

                                       var r = MathUtils.Lerp(aValue.Value, bValue.Value, t);
                                       return new InputValue<float>(r);
                                   }
                },
                {
                    typeof(Vector2), (a, b, t) =>
                                     {
                                         if (a is not InputValue<Vector2> aValue || b is not InputValue<Vector2> bValue)
                                             return null;

                                         var r = MathUtils.Lerp(aValue.Value, bValue.Value, t);
                                         return new InputValue<Vector2>(r);
                                     }
                },
                {
                    typeof(Vector3), (a, b, t) =>
                                     {
                                         if (a is not InputValue<Vector3> aValue || b is not InputValue<Vector3> bValue)
                                             return null;

                                         var r = MathUtils.Lerp(aValue.Value, bValue.Value, t);
                                         return new InputValue<Vector3>(r);
                                     }
                },
                {
                    typeof(Vector4), (a, b, t) =>
                                     {
                                         if (a is not InputValue<Vector4> aValue || b is not InputValue<Vector4> bValue)
                                             return null;

                                         var r = MathUtils.Lerp(aValue.Value, bValue.Value, t);
                                         return new InputValue<Vector4>(r);
                                     }
                },

                {
                    typeof(Quaternion), (a, b, t) =>
                                        {
                                            if (a is not InputValue<Quaternion> aValue || b is not InputValue<Quaternion> bValue)
                                                return null;

                                            var r = Quaternion.Slerp(aValue.Value, bValue.Value, t);
                                            return new InputValue<Quaternion>(r);
                                        }
                },
                {
                    typeof(int), (a, b, t) =>
                                 {
                                     if (a is not InputValue<int> aValue || b is not InputValue<int> bValue)
                                         return null;

                                     var r = MathUtils.Lerp(aValue.Value, bValue.Value, t);
                                     return new InputValue<int>(r);
                                 }
                },
                {
                    typeof(string), (a, b, t) =>
                                 {
                                     if (a is not InputValue<string> aValue2 || b is not InputValue<string> bValue2)
                                         return null;

                                     return new InputValue<string>(t <= 0.5f ? aValue2.Value : bValue2.Value);
                                 }
                },
                
                {
                    typeof(Int3), (a, b, t) =>
                                  {
                                      if (a is not InputValue<Int3> aValue || b is not InputValue<Int3> bValue)
                                          return null;

                                      var r = new Int3(MathUtils.Lerp(aValue.Value.X, bValue.Value.X, t),
                                                       MathUtils.Lerp(aValue.Value.Y, bValue.Value.Y, t),
                                                       MathUtils.Lerp(aValue.Value.Z, bValue.Value.Z, t)
                                                      );
                                      return new InputValue<Int3>(r);
                                  }
                },

                {
                    typeof(Int2), (a, b, t) =>
                                  {
                                      if (a is not InputValue<Int2> aValue || b is not InputValue<Int2> bValue)
                                          return null;

                                      var r = new Int2(MathUtils.Lerp(aValue.Value.Width, bValue.Value.Width, t),
                                                       MathUtils.Lerp(aValue.Value.Height, bValue.Value.Height, t)
                                                      );
                                      return new InputValue<Int2>(r);
                                  }
                },

                {
                    typeof(bool), (aValue, bValue, t) =>
                                  {
                                      if (aValue is not InputValue<bool> aValue2 || bValue is not InputValue<bool> bValue2)
                                          return null;

                                      return new InputValue<bool>(t <= 0.5f ? aValue2.Value : bValue2.Value);
                                  }
                },
                {
                    typeof(Gradient), (aGradient, bGradient, t) =>
                                      {
                                          if (aGradient is not InputValue<Gradient> aGradient2 || bGradient is not InputValue<Gradient> bGradient2)
                                              return null;

                                          Gradient gradientA = aGradient2.Value;
                                          Gradient gradientB = bGradient2.Value;

                                          // Blend if possible
                                          if (gradientA.Interpolation == gradientB.Interpolation
                                              && gradientA.Steps.Count == gradientB.Steps.Count)
                                          {
                                              Gradient newGradient = gradientA.TypedClone();
                                              for (int index = 0; index < gradientA.Steps.Count; index++)
                                              {
                                                  //Log.Debug("Blending gradient steps...");
                                                  var stepA = gradientA.Steps[index];
                                                  var stepB = gradientB.Steps[index];
                                                  newGradient.Steps[index].NormalizedPosition =
                                                      MathUtils.Lerp(stepA.NormalizedPosition, stepB.NormalizedPosition, t);
                                                  newGradient.Steps[index].Color = MathUtils.Lerp(stepA.Color, stepB.Color, t);
                                              }

                                              return new InputValue<Gradient>(newGradient);
                                          }

                                          // If not possible, just switch between the two gradients
                                          return t < 0.5 ? aGradient2.Clone() : bGradient2.Clone();
                                      }
                },
            };

    public static readonly Dictionary<Type, Func<InputValue, string>> ToStringMethods =
        new()
            {
                { typeof(float), v => v is not InputValue<float> vv ? string.Empty : $"{vv.Value:0.000}" },
                { typeof(Vector2), v => v is not InputValue<Vector2> vv ? string.Empty : $"{vv.Value.X:0.00} {vv.Value.Y:0.00}" },
                {
                    typeof(Vector3),
                    v => v is not InputValue<Vector3> vv ? string.Empty : $"{vv.Value.X:0.00} {vv.Value.Y:0.00} {vv.Value.Z:0.00} "
                },
                {
                    typeof(Vector4),
                    v => v is not InputValue<Vector4> vv
                             ? string.Empty
                             : $"{vv.Value.X:0.00} {vv.Value.Y:0.00} {vv.Value.Z:0.00} {vv.Value.W:0.00} "
                },
                { typeof(int), v => v is not InputValue<int> vv ? string.Empty : $"{vv.Value}" },
            };

    /// <summary>
    /// A set of functions that mix n values with blend factors
    /// </summary>
    /// <remarks>
    /// Note that Quaternions can't be easily weight blended.
    /// </remarks>
    public static readonly Dictionary<Type, Func<InputValue[], float[], InputValue>> WeightedBlendMethods =
        new()
            {
                {
                    typeof(float), (values, weights) =>
                                   {
                                       var sum = 0f;
                                       for (var index = 0; index < values.Length; index++)
                                       {
                                           var inputV = values[index];
                                           if (inputV is not InputValue<float> v)
                                               continue;

                                           sum += v.Value * weights[index];
                                       }

                                       return new InputValue<float>(sum);
                                   }
                },
                {
                    typeof(Vector2), (values, weights) =>
                                     {
                                         var sum = Vector2.Zero;
                                         for (var index = 0; index < values.Length; index++)
                                         {
                                             var inputV = values[index];
                                             if (inputV is not InputValue<Vector2> v)
                                                 continue;

                                             sum += v.Value * weights[index];
                                         }

                                         return new InputValue<Vector2>(sum);
                                     }
                },
                {
                    typeof(Vector3), (values, weights) =>
                                     {
                                         var sum = Vector3.Zero;
                                         for (var index = 0; index < values.Length; index++)
                                         {
                                             var inputV = values[index];
                                             if (inputV is not InputValue<Vector3> v)
                                                 continue;

                                             sum += v.Value * weights[index];
                                         }

                                         return new InputValue<Vector3>(sum);
                                     }
                },
                {
                    typeof(Vector4), (values, weights) =>
                                     {
                                         if (values.Length == 1)
                                             return values[0];

                                         var sum = Vector4.Zero;
                                         for (var index = 0; index < values.Length; index++)
                                         {
                                             var inputV = values[index];
                                             if (inputV is not InputValue<Vector4> v)
                                                 continue;

                                             sum += v.Value * weights[index];
                                         }

                                         return new InputValue<Vector4>(sum);
                                     }
                },
                {
                    typeof(int), (values, weights) =>
                                 {
                                     var sum = 0f;
                                     for (var index = 0; index < values.Length; index++)
                                     {
                                         var inputV = values[index];
                                         if (inputV is not InputValue<int> v)
                                             continue;

                                         sum += v.Value * weights[index];
                                     }

                                     return new InputValue<int>((int)(sum + 0.5f));
                                 }
                },
                {
                    typeof(string), (values, weights) =>
                                 {
                                     //var sum = 0f;
                                     var best = string.Empty;
                                     var bestWeight = float.NegativeInfinity;
                                     
                                     for (var index = 0; index < values.Length; index++)
                                     {
                                         var inputV = values[index];
                                         if (inputV is not InputValue<string> v)
                                             continue;
                                         
                                         var weight =  weights[index];
                                         if (!(weight > bestWeight)) 
                                             continue;

                                         bestWeight = weight;
                                         best = v.Value;
                                     }

                                     return new InputValue<string>(best);
                                 }
                },                
                {
                    typeof(Int2), (values, weights) =>
                                  {
                                      var sum = new Vector2();
                                      for (var index = 0; index < values.Length; index++)
                                      {
                                          var inputV = values[index];
                                          if (inputV is not InputValue<Int2> v)
                                              continue;

                                          sum += new Vector2(v.Value.Width * weights[index],
                                                             v.Value.Height * weights[index]);
                                      }

                                      return new InputValue<Int2>(new Int2((int)(sum.X + 0.5f),
                                                                           (int)(sum.Y + 0.5f)));
                                  }
                },
                {
                    typeof(Int3), (values, weights) =>
                                  {
                                      var sum = new Vector3();
                                      for (var index = 0; index < values.Length; index++)
                                      {
                                          var inputV = values[index];
                                          if (inputV is not InputValue<Int3> v)
                                              continue;

                                          sum += new Vector3(v.Value.X * weights[index],
                                                             v.Value.Y * weights[index],
                                                             v.Value.Z * weights[index]);
                                      }

                                      return new InputValue<Int3>(new Int3((int)(sum.X + 0.5f),
                                                                           (int)(sum.Y + 0.5f),
                                                                           (int)(sum.Z + 0.5f)));
                                  }
                },
                   
                {
                    typeof(Gradient), (gradients, weights) =>
                                      {
                                          var tempGradients = new List<Gradient>(gradients.Length);
                                          var bestIndex = -1;
                                          var bestWeight = float.PositiveInfinity;
                                          var isBlendable = true;

                                          for (var index = 0; index < gradients.Length; index++)
                                          {
                                              var inputV = gradients[index];
                                              if (inputV is not InputValue<Gradient> v)
                                                  continue;

                                              if (weights[index] < bestWeight)
                                              {
                                                  bestIndex = index;
                                                  bestWeight = weights[index];
                                              }

                                              if (index > 0)
                                              {
                                                  if (v.Value.Interpolation != tempGradients[0].Interpolation
                                                      || v.Value.Steps.Count != tempGradients[0].Steps.Count)
                                                  {
                                                      isBlendable = false;
                                                  }
                                              }

                                              tempGradients.Add(v.Value);
                                          }

                                          if (isBlendable)
                                          {
                                              var newGradient = tempGradients[0].TypedClone();
                                              for (var stepIndex = 0; stepIndex < tempGradients[0].Steps.Count; stepIndex++)
                                              {
                                                  var step = tempGradients[0].Steps[stepIndex];
                                                  var color = step.Color * weights[0];
                                                  var position = step.NormalizedPosition * weights[0];
                                                  for (var index = 1; index < tempGradients.Count; index++)
                                                  {
                                                      color += tempGradients[index].Steps[stepIndex].Color * weights[index];
                                                      position += tempGradients[index].Steps[stepIndex].NormalizedPosition * weights[index];
                                                  }
                                                      
                                                  newGradient.Steps[stepIndex].NormalizedPosition = position.Clamp(0,1);
                                                  newGradient.Steps[stepIndex].Color = color;
                                              }

                                              return new InputValue<Gradient>(newGradient);
                                          }

                                          return bestIndex >= 0
                                                     ? new InputValue<Gradient>(tempGradients[bestIndex])
                                                     : null;
                                      }
                },
            };

    public static readonly Dictionary<Type, Func<InputValue, InputValue, bool>> CompareFunctions =
        new()
            {
                {
                    typeof(float), (a, b) =>
                                   {
                                       if (a is not InputValue<float> aValue || b is not InputValue<float> bValue)
                                           return false;

                                       return Math.Abs(aValue.Value - bValue.Value) < float.Epsilon;
                                   }
                },
                {
                    typeof(Vector2), (a, b) =>
                                     {
                                         if (a is not InputValue<Vector2> aValue || b is not InputValue<Vector2> bValue)
                                             return false;

                                         return aValue.Value == bValue.Value;
                                     }
                },
                {
                    typeof(Vector3), (a, b) =>
                                     {
                                         if (a is not InputValue<Vector3> aValue || b is not InputValue<Vector3> bValue)
                                             return false;

                                         return aValue.Value == bValue.Value;
                                     }
                },
                {
                    typeof(Vector4), (a, b) =>
                                     {
                                         if (a is not InputValue<Vector4> aValue || b is not InputValue<Vector4> bValue)
                                             return false;

                                         return aValue.Value == bValue.Value;
                                     }
                },

                {
                    typeof(Quaternion), (a, b) =>
                                        {
                                            if (a is not InputValue<Quaternion> aValue || b is not InputValue<Quaternion> bValue)
                                                return false;

                                            return aValue.Value == bValue.Value;
                                        }
                },
                {
                    typeof(int), (a, b) =>
                                 {
                                     if (a is not InputValue<int> aValue || b is not InputValue<int> bValue)
                                         return false;

                                     return aValue.Value == bValue.Value;
                                 }
                },

                {
                    typeof(Int3), (a, b) =>
                                  {
                                      if (a is not InputValue<Int3> aValue || b is not InputValue<Int3> bValue)
                                          return false;

                                      return aValue.Value == bValue.Value;
                                  }
                },

                {
                    typeof(Int2), (a, b) =>
                                  {
                                      if (a is not InputValue<Int2> aValue || b is not InputValue<Int2> bValue)
                                          return false;

                                      return aValue.Value == bValue.Value;
                                  }
                },
            };

    public static string GetValueString(InputValue inputValue)
    {
        return inputValue switch
                   {
                       InputValue<float> f    => $"{f.Value:0.000}",
                       InputValue<int> i      => $"{i.Value:G3}",
                       InputValue<Int3> i     => $"{i.Value:G3}",
                       InputValue<bool> b     => $"{b.Value}",
                       InputValue<Vector3> v3 => $"{v3.Value:0.0}",
                       InputValue<Vector2> v2 => $"{v2.Value:0.0}",
                       InputValue<string> s   => s.Value.Truncate(),
                       _                      => ""
                   };
    }

    public static string GetValueString(IInputSlot inputSlot)
    {
            
        return inputSlot switch
                   {
                       InputSlot<float> f                    => $"{f.GetCurrentValue():0.000}",
                       InputSlot<int> i                      => $"{i.GetCurrentValue():G3}",
                       InputSlot<Int3> i                     => $"{i.GetCurrentValue():G3}",
                       InputSlot<bool> b                     => $"{b.GetCurrentValue()}",
                       InputSlot<System.Numerics.Vector3> v3 => $"{v3.GetCurrentValue():0.0}",
                       InputSlot<System.Numerics.Vector2> v2 => $"{v2.GetCurrentValue():0.0}",
                       InputSlot<string> s                   => s.GetCurrentValue().Truncate(),
                       _                                     => ""
                   };
    }

    public static string GetValueString(ISlot inputSlot)
    {
            
        return inputSlot switch
                   {
                       Slot<float> f                    => $"{f.Value:0.000}",
                       Slot<int> i                      => $"{i.Value:G3}",
                       Slot<Int3> i                     => $"{i.Value:G3}",
                       Slot<bool> b                     => $"{b.Value}",
                       Slot<System.Numerics.Vector3> v3 => $"{v3.Value:0.0}",
                       Slot<System.Numerics.Vector2> v2 => $"{v2.Value:0.0}",
                       Slot<string> s                   => s.Value.Truncate(),
                       _                                     => ""
                   };
    }
}