# Installing with development environment

For developing and debugging C# Operators we recommend setting up the development
environment and running it from an IDE (Integrated Development Environment). You have two options:

## A: Using Microsoft **Visual Studio (Community Edition)**

If you don't have a .NET IDE installed already, download and install [Microsoft Visual Studio Community Edition](https://visualstudio.microsoft.com/downloads/) 
- In the installer, make sure to select the features:
  - .NET Desktop Application development
  - .NET 4.7.1  (on the right side)
- Download and [.net SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) As of 2025-09-20 we use v9.0.203. But this version might have changed. Make sure to use the SDK, not the runtime.

## B: Using Jetbrains **Rider**

Rider is an excellent IDE for developing .net applications. It's free to use for non-commercial work like TiXL and its core development team uses Rider as the primary IDE.

- Download and install [Rider](https://www.jetbrains.com/rider/download/?section=windows)
- Download and [.net SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) As of 2025-09-20 we use v9.0.203. But this version might have changed. Make sure to use the SDK, not the runtime.

## Additional requirements

- You might also want to download and install a git client, like [git-fork](https://git-fork.com/). Alternatively, you can install the bare bone git scm.
- You also need to install [Windows Graphics tools](#Installation), although this will also installed by the TiXL releases, so it might already be installed.

## Cloning the repository.

You have many options.

### If you have a GitHub account

Ideally, it would be better to sign-up. It's free and only takes a minute or so. This allows you to share your changes with the community. If not, do the following:

- We recommend using ssh
- Make sure you have an ssh-key installed correctly. GitHub has [excellent documentation](https://docs.github.com/en/github/authenticating-to-github/connecting-to-github-with-ssh/adding-a-new-ssh-key-to-your-github-account) on this topic
- With Fork you just clone the repository

#### Github desktop
If you use a Github account, but are uncomfortable with terminals, Github provides a pretty good [desktop application](https://desktop.github.com/) for that matter.

### A: Using a git-scm-software:

- Open the github repository and copy the link the main branch:

<img width="555" height="459" alt="image" src="https://github.com/user-attachments/assets/f473c700-52aa-49ff-bce1-356156faa175" />

```
git@github.com:tixl3d/tixl.git
```

- In you git-scm, select File -> Clone Repository and paste the copied url. 
- Select or create the folder, where you want to develop, e.g. `<Home>\Documents\Dev\`


### B: Using a terminal window

- Open a terminal window or git bash in the folder where you want to install TiXL. The easiest way to do this is by navigating to the folder in File Explorer, then typing `cmd` into the address bar.
- Clone the repository:

```
git clone git@github.com:tixl3d/tixl.git
```
*Note: if this operation fails, you may not have correctly configured your GitHub SSH key - see [GitHub's documentation](https://docs.github.com/en/github/authenticating-to-github/connecting-to-github-with-ssh/adding-a-new-ssh-key-to-your-github-account). You can also clone the repository using HTTPS instead.*

## Running TiXL

1. Open the t3.sln
2. Press run - On the first start, the IDE will have to fetch all dependencies and packages which can take a minute or so.
3. The editor should start as expected.

----

Read next: 

- [Running TiXL from an IDE](dev.UsingDev)
- [Writing c# Ops](dev.WritingCodeOps)




