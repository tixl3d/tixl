---
id: csv-to-points
title: CSV to Points Router
scope: graph-window
tags: [smoke, user, point-buffer, csv]
added: 2026-11-01
added-in-version: 4.4
prerequisites:
  - A project with an empty graph is open.
  - A CSV file with a header row is available.
---

`CsvToPoints` routes CSV columns into the attributes of a point buffer. This set checks the file load, the per-attribute dropdowns, that remapping a column updates the buffer, and the shipped example.

## Step: Opening the example

**Action:**
Open the symbol browser, find `CsvToPointsExample` in `Examples.Lib.point.io`, and add it to the graph.

**Expected:**
- The output shows a colorful 21×21 ripple of points.
- The parameter panel offers a `CsvFile` input pointing at the bundled `point-data/csv-to-points-grid.csv`.

## Step: Adding the operator

**Action:**
Open the symbol browser, search for `CsvToPoints`, and add it to the graph.

**Expected:**
- The operator shows a `Points` output and inputs grouped into CSV File, Rotation, Position, Color, Scale and Custom Attributes.

## Step: Loading the CSV

**Action:**
Set `FilePath` to the CSV file and open its parameter panel.

**Expected:**
- The status reports the number of loaded points and mapped attributes.
- Each attribute dropdown lists `(none)` followed by the CSV's header names.

## Step: Routing a column

**Action:**
Set `PositionX` to a numeric column, then connect `Points` to a point visualizer such as `DrawPoints`.

**Expected:**
- The point positions change to the values of the selected column.
- Selecting `(none)` restores position 0 for that axis.
