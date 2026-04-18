Please contribute and edit this page. ♥

# General

## I've followed the installation instructions, but TiXL still won't start. What can i do?
Installing Visual C++ Redistributable might help: [vc_redist.x64](https://aka.ms/vs/17/release/vc_redist.x64.exe)
(from https://learn.microsoft.com/en-US/cpp/windows/latest-supported-vc-redist?view=msvc-170)

## What is the difference to Touch Designer?
To be honest, I am not really familiar with TD. From what I have seen both applications use a Directed Acyclic Graph (just like Houdini, Maya, Grasshopper and countless other node based programs). Information flows from left to right and both rely heavily on visualizing texture outputs. What might set TiXL apart is its extensive use of key frame animations and time manipulation possibilities. It is also very very good for live coding with Shaders and C#. Also, since TiXL has its origins in the [Demoscene](https://en.wikipedia.org/wiki/Demoscene), it can export small size executables. 

As far as I know TD has a huge community and tons of features and is probably much more stable, great documentation and support -- all the things you would expect from a mature commercial product.

The two might look similar on the surface which might make the transition between the two easy. But I would be surprised if the similarities run very deep.

## So, where are the Mac/Linux versions?

We can totally understand this question. We work with Macs, too. And since the latest hardware push with the M1 silicon, Apple once again became an very promising platform.

At the moment however we are focusing on a single use case and moving fast. For this DirectX11 is the ideal API because it fits the sweet spot between performance, features and established development Tools like [RenderDoc](/still-scene/t3/wiki/using-renderdoc).

In the long run the picture looks different: TiXL is made with ImGui and .netcore. Both technologies are available for Linux and Mac. In theory porting the user interface layer should be possible in a weekend or so. A completely different story are all compute-, image- and rendering  effects. Vulkan is a vastly more complex and verbose API than DirectX. So porting these would be a very big undertaking. Questions like backward compatibility to older TiXL projects or support for multiple rendering APIs are two more question that we need to address once the time has come to make the transition.

If you have a background in .net and Vulkan helping us on that front might be the perfect opportunity to contribute.

If you're using Linux, you can already use TiXL today: See [Install on Linux](help.InstallLinux)



## Is there a stand-alone version
New Standalone Version available with significant updates and quality of life improvements...

Get the latest Standalone version here → https://github.com/still-scene/t3/releases/

At the moment, you still require:

- [Windows Graphics tools](https://docs.microsoft.com/en-us/windows/uwp/gaming/use-the-directx-runtime-and-visual-studio-graphics-diagnostic-features)
- [.net 4.7.1](http://go.microsoft.com/fwlink/?linkid=852092)
- [.net 5](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-5.0.408-windows-x64-installer)

# How To

## Updating TiXL 

We invested a lot of care and to design a software architecture and data format that maintains backwards compatibility when updating. So if you use the following guide lines updating with git should „just work“:

- Never modify Symbols in the `lib.*` namespace without submitting a pull request.
- Don’t work directly in the [Dashboard] operator -- create your own playground or project ops instead.
- Use a consistent namespace for all your Operators like `user.yourname.project` .
- Carefully separate your personal resource files. We suggest to create folders like `Resourced/user/yourname/projectTitle/` for this.

If you are using git pulling the latest version from master should work without a hitch. There is a [video tutorial](https://www.youtube.com/watch?v=nzfrzOHZr7g) demonstrating how it works.

If you are using a stand alone version, you would need to move...
- all files in .t3/ 
- Your resources 
- And you custom ops to the new version.

In the Long run this method should be improved with more comfortable migration features.


## Exporting an executable
See [export executables](help.ExportExecutables)

## Export Image-Sequence
See [Exporting Videos](help.ExportVideos)

## TiXL won't doesn't start
We're sad to hear that. But not all is lost. TiXL automatically saves backups every 3 minutes.
[Read more about backups](help.Backups)


### Fixing startup problems

Possible reason for startup problems can be...
 
- A crash while saving lead to incomplete or empty files in `/Types/Operators`.
- The internal state of an operator got corrupted and saved.
