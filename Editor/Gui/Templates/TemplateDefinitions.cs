namespace T3.Editor.Gui.Templates;

static class TemplateDefinitions
{
    // FIXME: these need to be fixed
    public static readonly IReadOnlyList<TemplateDefinition> Templates
        = new List<TemplateDefinition>()
              {
                  // new()
                  //     {
                  //         Title = "Point Compute Shader",
                  //         DefaultSymbolName = "NewPointShader",
                  //         Summary = "Creates a compute shader setup",
                  //         Documentation =
                  //             "This will create a new Symbol with an fully working compute shader example. It will setup and open the hlsl shader source code.",
                  //         TemplateSymbolId = Guid.Parse("0db659a4-d0ba-4d23-acac-aea5ba5b57dc"),
                  //         AfterSetupAction = (newInstance, name, nameSpace, description, resourceFolder) =>
                  //                            {
                  //                                Directory.CreateDirectory(Path.Combine(resourceFolder, "shader"));
                  //
                  //                                // Duplicate and assign new shader source file
                  //                                try
                  //                                {
                  //                                    var newShaderFilename = $@"{resourceFolder}shader\{name}.hlsl";
                  //                                    var shaderInstance = newInstance.Children.Values.SingleOrDefault(c => c.Symbol.Id ==
                  //                                                     Guid.Parse("a256d70f-adb3-481d-a926-caf35bd3e64c"));
                  //
                  //                                    File.Copy(@"examples\templates\PointShaderTemplate.hlsl",
                  //                                              newShaderFilename);
                  //
                  //                                    if (shaderInstance is ComputeShader computeShader)
                  //                                    {
                  //                                        computeShader.Source.TypedInputValue.Value = newShaderFilename;
                  //                                        computeShader.Source.DirtyFlag.Invalidate();
                  //                                        computeShader.Source.Input.IsDefault = false;
                  //                                    }
                  //                                    else
                  //                                    {
                  //                                        Log.Warning("Can't find compute shader for source file");
                  //                                    }
                  //                                   
                  //                                    CoreUi.Instance.OpenWithDefaultApplication(newShaderFilename);
                  //                                }
                  //                                catch (Win32Exception e)
                  //                                {
                  //                                    Log.Warning("Assigning new shader failed: " + e.Message);
                  //                                }
                  //                            }
                  //     },
                  //
                  // new()
                  //     {
                  //         Title = "Image Effects Shader",
                  //         DefaultSymbolName = "NewImageFxShader",
                  //         Summary = "Creates a pixel shader setup",
                  //         Documentation =
                  //             "This will create a new Symbol with an fully working pixel (I.e. fragment) shader example. It will setup and open the hlsl shader source code.",
                  //         TemplateSymbolId = Guid.Parse("fdd58452-ecb4-458d-9f5b-9bce356d5125"),
                  //         AfterSetupAction = (newInstance, name, nameSpace, description, resourceFolder) =>
                  //                            {
                  //                                Directory.CreateDirectory(Path.Combine(resourceFolder, "shader"));
                  //
                  //                                // Duplicate and assign new shader source file
                  //                                try
                  //                                {
                  //                                    var newShaderFilename = $@"{resourceFolder}shader\{name}.hlsl";
                  //                                    var shaderSetupInstance = newInstance.Children.Values.SingleOrDefault(c => c.Symbol.Id ==
                  //                                                     Guid.Parse("bd0b9c5b-c611-42d0-8200-31af9661f189"));
                  //
                  //                                    File.Copy(@"examples\templates\ImgFxShaderTemplate.hlsl",
                  //                                              newShaderFilename);
                  //
                  //                                    if (shaderSetupInstance is _ImageFxShaderSetupStatic shaderSetup)
                  //                                    {
                  //                                        shaderSetup.Source.TypedInputValue.Value = newShaderFilename;
                  //                                        shaderSetup.Source.DirtyFlag.Invalidate();
                  //                                        shaderSetup.Source.Input.IsDefault = false;
                  //                                    }
                  //                                    else
                  //                                    {
                  //                                        Log.Warning("Can't find pixel shader for source file");
                  //                                    }
                  //
                  //                                    // Open editor
                  //                                    CoreUi.Instance.OpenWithDefaultApplication(newShaderFilename);
                  //                                }
                  //                                catch (Exception e)
                  //                                {
                  //                                    Log.Warning("Assigning new shader failed: " + e.Message);
                  //                                }
                  //                            }
                  //     },
              };
}