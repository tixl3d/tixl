# C# Delaunay triangulation + Voronoi Diagram

A C# implementation of the [Bowyer–Watson algorithm](https://en.wikipedia.org/wiki/Bowyer%E2%80%93Watson_algorithm).
The result is a [Delaunay triangulation](https://en.wikipedia.org/wiki/Delaunay_triangulation) for a set of randomly generated points.
Following the Delaunay triangulation, the dual [Voronoi diagram](https://en.wikipedia.org/wiki/Voronoi_diagram) is constructed.

A screenshot of the Delaunay triangulation and the Voronoi diagram for 5000 points.

<img alt="Delaunay triangulation and Voronoi diagram for 5000 points" src="screenshots/delaunay_voronoi.png" width="700">

## Why C#?

It just looks good. Also, blog posts listed below talking about procedural content and map generation caught my eye.
Since my intention is to port the algorithms to the [Unity game engine](https://unity3d.com/) for future projects, I decided to stick to C#, as it is Unity's scripting language of choice.

## License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details

## Acknowledgments

* [Procedural Dungeon Generation Algorithm](https://www.gamasutra.com/blogs/AAdonaac/20150903/252889/Procedural_Dungeon_Generation_Algorithm.php)
* [Polygonal Map Generation for Games](http://www-cs-students.stanford.edu/~amitp/game-programming/polygon-map-generation/)
* [Check if point is in circumcircle of a triangle (TitohuanT's answer)](https://stackoverflow.com/questions/39984709/how-can-i-check-wether-a-point-is-inside-the-circumcircle-of-3-points)
