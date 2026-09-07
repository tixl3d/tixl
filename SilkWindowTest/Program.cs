using System.Numerics;
using SilkWindows;
using SilkWindows.Implementations.FileManager;
using T3.Core.SystemUi;

var windowProvider = new SilkWindowProvider();
ImGuiWindowService.Instance = windowProvider;
BlockingWindow.Instance = windowProvider;

BlockingWindow.Instance.ShowMessageBox("this is text!!!!", "this is a title!!!!", "this is a button!!!!", "this is a button!!!!", "this is a button!!!!");

ManagedDirectory[] directories = [new ManagedDirectory("C:/Users/Dom/Desktop", true, true, "Your damn desktop"), new ManagedDirectory("C:/Users/Dom/AppData/Roaming", true), new ManagedDirectory("C:/Users/Dom/Downloads", false)];
var path = ImGuiWindowService.Instance.Show(title: "File dropper",
                          drawer: new FileManager(FileManagerMode.PickFile, directories),
                          options: new SimpleWindowOptions(Vector2.One * 600, 60, true, true, true));

Console.WriteLine($"Got \"{path}\"");