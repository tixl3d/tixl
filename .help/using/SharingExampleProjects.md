# Creating and Using Example Projects for TiXL

## Creating an Example Project

Sharing example projects is a great way to contribute to the community. Ideally, these projects should be provided as a zip archive of the project folder in your projects directory. For instance, sharing a project called `AbcDemo` could look like this:

in `Documents/TiXL/`...

```
  AbcDemo/
    bin <- Do NOT include this folder because it will be recreated on startup
    obj <- Do NOT include this folder because it will be recreated on startup
    Resources/
      shaders/
        someshader.hlsl
      images/
        texture.jpg
        logo.png
      soundtrack/
        soundtrack.mp3

    AbcDemo.csproj
    AbcDemo.t3ui
    AbcDemo.t3
    AbcDemo.cs   

    README.md
```

Make sure that all asset references use local resource paths (e.g. `AbcDemo/images/logo.png`) and don't rely on files outside of the Resources folder.

It is also a great idea to include a `README.md` file with a short description, author credits, and a license specifying what uses are allowed for the content.

It is a good habit to include annotations (Shift+A) and operator comments (Ctrl+Shift+C) in your graphs. The HowTo examples are a good reference for how this could look.

## Using an Example Project

Unzip the project archive into your TiXL projects folder.

⚠ Make sure to only add the example once, because including multiple copies of the same operator can corrupt TiXL's data structure.
