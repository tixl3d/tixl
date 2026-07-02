The master timeline position that the transport controls advance — the shared clock the whole project animates against by default.

Like all time in TiXL it's counted in musical bars rather than seconds, so it stays aligned to a project's BPM and soundtrack. Individual branches can diverge from it through [ui:TimeOverrides|time clips or overrides], but playback time is the common reference the [ui:Timeline] marker shows. Use [SetPlaybackTime] when you need to move the whole project's clock from within the graph.
