# Background information:

There are many ways deal with text and render fonts. Internally TiXL already processes texts with UTF8. But these strings have to be visible in the TiXL Editor UI and eventually be rendered by TiXL with operators likes [lib.3d.Text]. 

## The editor

TiXL's GUI is implemented with [Dear ImGui](https://github.com/ocornut/imgui) and uses a font called [Roboto](https://fonts.google.com/specimen/Roboto). The UI font is processed on every startup into a font atlas texture. You can check this texture by using "Window→ImGUI Demo / Customization / Fonts / Atlas Texture". So extending [support of other character ranges](https://github.com/ocornut/imgui/blob/master/docs/FONTS.md#using-custom-glyph-ranges) likes [Chinese](https://github.com/tixl3d/tixl/issues/264), Korean or Japanese should be possible and some people succeed with forks for that. Although the base-set for Chinese characters fits into a 16k atlas texture, there are some internal ImGui exceptions when trying to apply that list. More work is needed here.

## Rendering Texts

There are many technical methods for rendering characters in a 3d application. TiXL primarily uses a method called [Multi-Channel Signed Distance Field](https://github.com/Chlumsky/msdfgen#readme) (MSDF). It combines the benefits of sharp corners and edges at very larges sizes (1000px and more) with relatively small atlas textures size and good rendering performance. It main disadvantage is the lack of support of colors fonts (like emojis).

For MSDF to work, you have to generate an Atlas-texture together with a description file that defines where each of your character is within the texture. This description file uses an [established format](https://www.angelcode.com/products/bmfont/doc/file_format.html) and also includes information for kerning (but sadly not advanced typography like ligatures).

The software for generating new fonts is a free and open source and works for all True Type fonts and character ranges. Once you installed the software generating the atlas texture takes less than a minute.


# Converting fonts to MSDF 

## Tixl 4.1 internal MSDF generation tool.
Find it here: Tixl menu -> Development tools -> Uitilities -> Msdf Generation
1. Select the font file you want to convert.
2. Choose the project where the .fnt and .png files will be generated.
3. Click "Generate MSDF".
<img width="550" height="307" alt="image" src="https://github.com/user-attachments/assets/4a513ac5-ec69-46bc-bcce-d67f3b345b8f" />

## Legacy method (still valid) 
1. Install converter
    1. Install [Node](https://nodejs.org/en/download/) and the “necessary tools” coming with its installer (this might take several minutes)
    2. Install https://soimy.github.io/msdf-bmfont-xml/

```
npm install msdf-bmfont-xml -g
```

2. Open terminal and change into directory
```
mkdir fontConversion
cd fontConversion
```

3. Download character set (Using the 212 characters of the extended ascii character table might be excessive):
https://www.dropbox.com/s/paunc0qyo8alys7/eascii%20%281%29.txt?dl=1
  - Alternatively you can copy and paste the following line into a file called `eascii.txt` (Don't forget to include the leading space):
```
 !'"#$%&()*,=+-./0123456789:;<>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\]^_`abcdefghijklmnopqrstuvwxyz{|}~€‚ƒ„…ˆŠ‹ŒŽ‘’“”•–—˜™š›œžŸ¡¢£¤¥¦§¨©ª«¬­®¯°±²³´µ¶·¸¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ
```

4. Download fonts (e.g. from fonts.google.com)

Download and adjust the following bat:

```
:: Create atlas texture with...
::
:: -m 1024px dimension → Use 2048 for fonts with fine structures or curves 
:: -s 80 size → Increase if too much empty space in created image; decrease if two images were created
:: -r 4 Distance range → This will impact the softness of the font rendering and range of shadow. Too low causes aliasing. Too high artifacts.
:: -p 10 padding → I recommend 10 for 2048
::
:: Replace the <YourFontName> with the ttf filename
:: After conversion only copy .fnt and .png to your TiXL-Resources folder

msdf-bmfont --reuse -i eascii.txt -m 1024,1024 -s 80 -r 4 -p 4 -t msdf -o <YourFontName>.png <YourFontName>.ttf
```

- Optimize size and radius parameters to nicely fill a single atlas file.
- Copy the `.fnt` and `.png` to `T3/Resources/fonts/` folder.
- Select `.fnt` file for [Text] operator.

## Converting fonts to MSDF by using [MSDFtron](https://github.com/newemka/MSDFtron)
(a user interface for the precedent workflow) 

![image](https://github.com/user-attachments/assets/3bd9adbe-4836-435e-94a2-2dd4fbadfa8b)

1. Download and unpack MSDFtron [https://github.com/newemka/MSDFtron](https://github.com/newemka/MSDFtron)
2. Run msdftron.exe 
3. Select a font file
4. Click the convert button (MSDFtron default settings should work in most cases). 
5. Check the generated `.png` file (make sure it didn't generate more than one `.png`). 
6. Adjust MSDFtron parameters (try to reduce empty space). 
7. Place the `.png` and `.fnt` files in the Resources folder of your project.
8. Select your brand new `.fnt` file for [Text] operator.

