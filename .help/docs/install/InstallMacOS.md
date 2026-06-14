# Install on MacOS

This guide covers running TiXL on MacOs under Sikarugir.

## Background
We are working on MacOS support, but this is a major task that requires multiple drastic refactoring efforts. We have already made large strides in separating Windows dependencies from the .NET architecture. However, moving from DirectX to Vulkan and supporting abstraction layers for Metal will be another significant undertaking.

## Running with ~~Kegworks~~ Sikarugir

Sikarugir (which was formerly called "Kegworks", but was forced to rename because of that name was trademarked) is a convenient open source tool to run Wine on Mac OS X. [Please follow the installation instructions on the Sikarugir repository](https://github.com/Sikarugir-App/Sikarugir), which in turn requires either [MacPorts](https://www.macports.org/install.php) or [Homebrew](https://brew.sh/).

### Sikarugir setup

First, setup Sikarugir / Winery:

- install a new engine by pressing the plus icon below the list, selecting the engine and pressing 'Download and install' (`v23.7.1` and `v24.0.7` seem to be working well at the time of writing)
- install the wrapper by pressing 'Update Wrapper'

<img width="516" alt="Screenshot 2025-03-15 at 13 26 53" src="https://github.com/user-attachments/assets/fa1280c2-90f5-475d-80dd-55c8f600ac6b" />

<img width="431" alt="image" src="https://github.com/user-attachments/assets/5625716c-2975-4c62-af1c-911d9d77fd92" />


### Setting up the Sikarugir Wrapper

- click the 'Create new blank wrapper' button
- name the application (for example: Tooll3)
  - This will hang the window for a while. It may take a few minutes while the window is unresponsive.
  - The wrapper app will be placed in your userprofile: `~/Applications/Sikarugir/Tooll3.app`, `/Users/[name]/Applications/Sikarugir/Tooll3.app`
  - Once finished, you will see a dialog confirming the creation and you can open it
- you now are required to install some wintricks, by clicking the Winetricks button:

TiXL:
  - `dlls/d3dcompiler_43`
  - `dlls/d3dcompiler_47`
  - `fonts/corefonts`

Tooll3:
  - `dlls/d3dcompiler_43`
  - `dlls/d3dcompiler_47`
  - `dlls/dotnetdesktop6` ⚠
  - `fonts/corefonts`

<img width="410" alt="image" src="https://github.com/user-attachments/assets/2ab6496d-c644-42c3-ae32-603160e52706" />

<img width="410" alt="image" src="https://github.com/user-attachments/assets/260df6d0-8b70-4a2f-9da0-20fa4ccc2a53" />

<img width="842" alt="image" src="https://github.com/user-attachments/assets/75e1c7ae-706b-4254-9594-b4b8335a9dac" />

### Installing Tooll3

- Download and unzip the current stable Tooll3 release (v3.9.3 at the time of writing): https://github.com/tixl3d/tixl/releases/
- Go to the Application folder (`~/Applications/Sikarugir/`, `/Users/[name]/Applications/Sikarugir/Tooll3.app`)
- Right-click (control click) the app and select the `show package contents` option
- Drag and drop the unpacked Tooll3 folder into `Contents/drive_c/`

<img width="939" alt="image" src="https://github.com/user-attachments/assets/f34c7219-6517-4666-a5e7-f4aed6cc5c5e" />

![out](https://github.com/user-attachments/assets/bce41f3e-3922-4b40-ae5d-22f21bafd3d9)

### Finishing the Wrapper configuration

- Open your created `Tooll3.app` again
- Click on advanced
- Set the executable by pressing the "Browse" button to `"C:\TiXL-v3.9.3\StartT3.exe"` by navigating to the pasted folder
- Set `Direct3D to Metal translation layer` option
- (optionally change the [logo](https://tooll.io/images/T3-logo.png))
- click on 'Test Run'

Tooll3 should now start successfully on your Mac. If you run into any issues, feel free to reach out by creating a [new issue in this repository](https://github.com/tixl3d/tixl/issues/new/choose).

<img width="797" alt="image" src="https://github.com/user-attachments/assets/c3613ca5-8868-4171-8282-1f6d09bc644a" />


### Installing TiXL
- Download latest release exe from https://github.com/tixl3d/tixl/releases
- Find your TiXL container in the `/Applications/Sikarugir/` folder and 
- Double click TiXL

<img width="919" height="439" alt="image" src="https://github.com/user-attachments/assets/81d7a26a-c854-4aad-b41d-c18370c30918" />

- In Configuration run click Install Software.
- Select _Choose Setup Executable_ ...

<img width="619" height="364" alt="image" src="https://github.com/user-attachments/assets/843952ec-90e6-402f-b542-4a36ad3fd6f7" />

- Select your downloaded release (probably in Downloads):

<img width="801" height="449" alt="image" src="https://github.com/user-attachments/assets/51d21c2a-9434-4c5f-8ce9-01b32570b030" />

The TiXL installer will magically appear:

<img width="228" height="162" alt="image" src="https://github.com/user-attachments/assets/ded77742-022a-4dfd-bd97-9f49e2fab85e" />

<img width="599" height="460" alt="image" src="https://github.com/user-attachments/assets/655e7213-bc6c-428f-94fe-ac39c04a52bf" />


Do _not_ change the target folder and click _Next_:

<img width="602" height="465" alt="image" src="https://github.com/user-attachments/assets/681ff474-72aa-48ae-8040-e200aaff2266" />


This will install:

<img width="602" height="464" alt="image" src="https://github.com/user-attachments/assets/3bd386e6-a0f8-4c0e-9f99-0cd3ef53439a" />

If .net9 has already been installed, it will ask to repair. In this case, click _Close_ and confirm with _Yes_:

<img width="658" height="494" alt="image" src="https://github.com/user-attachments/assets/e1f8606b-5b37-4673-812e-a6bb951b0650" />
<img width="665" height="487" alt="image" src="https://github.com/user-attachments/assets/2d4e43e3-f9bc-4d01-a739-992990e04fd7" />


Finally click _Finish_ with the Launch checkbox enabled:

<img width="600" height="463" alt="image" src="https://github.com/user-attachments/assets/ccc93d84-0da7-47d3-b59b-4c605ffc8dc8" />


Change the windows app path by clicking _Browse_:

<img width="691" height="368" alt="image" src="https://github.com/user-attachments/assets/5e7f2a2c-02b0-4bfb-9c74-7af912f96f85" />

Select TiXL:

<img width="1049" height="562" alt="image" src="https://github.com/user-attachments/assets/f181b4f8-804e-47fa-86a8-8c3e7594b1e1" />


Start with _Test Run_.

 
### Optional: Allow Retina rendering

- Open the advanced settings of the wrapper
- Go into the 'Tools' tab
- Click the 'Registry Editor' button
- Open `HKEY_CURRENT_USER\Software\Wine\Mac Driver`
- Right-Click in the main pane `New > String Value`
- Name the key `RetinaMode` and the value `Y` **(capitalization is important)**

<img width="1297" alt="image" src="https://github.com/user-attachments/assets/936c8c32-ae61-422c-a546-3052ebb3cd0e" />

![image](https://github.com/user-attachments/assets/9aea310e-e62d-4f88-ac1c-0ec424b2ef31)
