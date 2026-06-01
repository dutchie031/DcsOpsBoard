 using System.Text.Json;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Text;
using _2dTileExporter.Models;
using _2dTileExporter.Readers;
using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;
using DcsOpsBoard.Types;
using Mapbox.Vector.Tile;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Union;
using Coordinate = Mapbox.Vector.Tile.Coordinate;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;

namespace _2dTileExporter.Exporters;

public class SettlementHullExporter(Map map, int minZoom, int maxZoom, string sourceFile, string outputDirectory)
{
	// Goal: export deterministic populated-area polygons for real settlements by
	// grouping buffered building geometry, projecting it, clipping it to tiles,
	// validating the written rings, and emitting coverage metadata.
	// Minimum number of linked building candidates needed to emit a populated-area polygon.
	private const int MinBuildingsPerSettlement = 10;
	// Smallest final settlement polygon area (m^2) to keep; lower values include tiny hamlets.
	private const double MinSettlementAreaSqm = 400;
	// Buffer distance inputs for grouping nearby buildings into a shared settlement geometry.
	private const double OccupancyCellSizeMeters = 20;
	// Additional geometry expansion before union to avoid fragmented town envelopes.
	private const int HullDilationCells = 3;
	// Building candidate pre-filter: minimum footprint area in m^2.
	private const double MinBuildingAreaSqm = 4;
	// Building candidate pre-filter: minimum shortest side in meters.
	private const double MinBuildingMinSideMeters = 3;
	private const uint TileExtent = 4096;

	private static readonly string[] ExcludedTypeTokens =
	[
		"POLE",
		"WIRE",
		"BRIDGE"
	];

	private readonly CoordConverter coordConverter = CoordConverter.CreateForMap(map.ToMapString());
	private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(1.0));
	private bool validatedSampleTile;
	private ulong settlementId;

	// Simple profiling aggregates (Stopwatch ticks)
	private static readonly ConcurrentDictionary<string, long> profileTicks = new();
	private static readonly ConcurrentDictionary<string, long> profileCounts = new();
	// Diagnostics for write-phase: skip reasons and feature counts
	private static readonly ConcurrentDictionary<string, long> writeDiagnostics = new();
	private static long totalFeaturesWritten = 0;
	// Sampled diagnostics for skipped features and settlement-build failures
	private static readonly ConcurrentBag<string> writeSkipSamples = new();
	private static readonly ConcurrentBag<string> traceFailSamples = new();

	private static void ProfileRecord(string key, long ticks)
	{
		profileTicks.AddOrUpdate(key, ticks, (_, v) => v + ticks);
		profileCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
	}

	private static void PrintProfiles()
	{
		Console.WriteLine("[Profile] Timings (ms total, calls):");
		foreach (var kv in profileTicks.OrderByDescending(kv => kv.Value))
		{
			string name = kv.Key;
			long ticks = kv.Value;
			profileCounts.TryGetValue(name, out long calls);
			Console.WriteLine($"  {name}: {ticks / TimeSpan.TicksPerMillisecond} ms, {calls} calls");
		}
	}

	public async Task Export()
	{
		try
		{
			Console.WriteLine($"Reading object source file: {sourceFile}");
			List<MapObject> objects = ObjectSourceFileReader.ReadObjects(sourceFile);
			Console.WriteLine($"Loaded {objects.Count} objects.");

			var buildResult = BuildSettlements(objects);
			List<SettlementCandidate> settlements = buildResult.Settlements;
			Console.WriteLine($"Built {settlements.Count} settlement hulls.");
			ProfileRecord("Export.BuildSettlements", 0);
			double coverPct = buildResult.TotalCandidateCount > 0 ? (100.0 * buildResult.UsedBuildingCount / buildResult.TotalCandidateCount) : 0.0;
			Console.WriteLine($"[Profile] BuildSettlements produced {settlements.Count} settlements. Buildings covered: {buildResult.UsedBuildingCount}/{buildResult.TotalCandidateCount} ({coverPct:F2}%)");

			if (Directory.Exists(outputDirectory))
			{
				Directory.Delete(outputDirectory, recursive: true);
			}
			Directory.CreateDirectory(outputDirectory);

			for (int zoom = minZoom; zoom <= maxZoom; zoom++)
			{
				try
				{
					Console.WriteLine($"Exporting settlement hulls for z{zoom}...");
					var swAssign = Stopwatch.StartNew();
					using var threadLocal = new ThreadLocal<Dictionary<(int x, int y), List<SettlementTileData>>>(() => new Dictionary<(int x, int y), List<SettlementTileData>>(), trackAllValues: true);
					Parallel.ForEach(settlements, settlement =>
					{
						try
						{
							List<LatLong> footprint = ProjectFootprint(settlement.Footprint);
							if (footprint.Count < 3) return;

							Bounds bounds = GetBounds(footprint);
							HashSet<(int tileX, int tileY)> tilesCovered = GetIntersectingTiles(bounds, zoom);
							var local = threadLocal.Value!;
							foreach ((int tileX, int tileY) in tilesCovered)
							{
								var candidateTile = new TileData
								{
									MinLat = TileYToLat(tileY + 1, zoom),
									MaxLat = TileYToLat(tileY, zoom),
									MinLon = TileXToLon(tileX, zoom),
									MaxLon = TileXToLon(tileX + 1, zoom)
								};

								if (!TryBuildTileGeometry(footprint, candidateTile, TileExtent, out List<List<Coordinate>>? tileGeometry))
								{
									writeDiagnostics.AddOrUpdate("skip.assignment", 1, (_, v) => v + 1);
									continue;
								}

								var key = (tileX, tileY);
								if (!local.TryGetValue(key, out var list))
								{
									list = new List<SettlementTileData>();
									local[key] = list;
								}
								list.Add(new SettlementTileData
								{
									BuildingCount = settlement.BuildingCount,
									Footprint = footprint,
									TileGeometry = tileGeometry!,
									OriginalHull = settlement.Footprint
								});
							}
						}
						catch (Exception ex)
						{
							throw new InvalidOperationException($"Assignment failed for z{zoom} settlement buildings={settlement.BuildingCount} hullPts={settlement.Footprint.Count}: {ex.Message}", ex);
						}
					});

					var tiles = new Dictionary<(int x, int y), TileData>();
					foreach (var local in threadLocal.Values)
					{
						if (local is null) continue;
						foreach (var kvp in local)
						{
							if (!tiles.TryGetValue(kvp.Key, out var td))
							{
								td = new TileData { MinLat = TileYToLat(kvp.Key.Item2 + 1, zoom), MaxLat = TileYToLat(kvp.Key.Item2, zoom), MinLon = TileXToLon(kvp.Key.Item1, zoom), MaxLon = TileXToLon(kvp.Key.Item1 + 1, zoom) };
								tiles[kvp.Key] = td;
							}
							td.Settlements.AddRange(kvp.Value);
						}
					}
					swAssign.Stop();
					ProfileRecord("Export.ProjectAndAssignTiles", swAssign.ElapsedTicks);
					Console.WriteLine($"Prepared {tiles.Count} tiles for z{zoom} in {swAssign.ElapsedMilliseconds} ms. Starting write phase...");

					var swWrite = Stopwatch.StartNew();
					var writeTasks = new List<Task>();
					using (var semaphore = new SemaphoreSlim(Environment.ProcessorCount))
					{
						foreach (KeyValuePair<(int x, int y), TileData> kvp in tiles)
						{
							semaphore.Wait();
							var tileKey = kvp.Key; var tileValue = kvp.Value;
							writeTasks.Add(Task.Run(async () =>
							{
								try
								{
									await WriteTile(zoom, tileKey.x, tileKey.y, tileValue);
								}
								catch (Exception ex)
								{
									throw new InvalidOperationException($"WriteTile failed for z{zoom}/{tileKey.x}/{tileKey.y}: {ex.Message}", ex);
								}
								finally { semaphore.Release(); }
							}));
						}

						try
						{
							Task.WaitAll(writeTasks.ToArray());
						}
						catch (AggregateException ex)
						{
							Exception flattened = ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex;
							Console.WriteLine($"[ExportError] z{zoom} write phase failed: {flattened}");
							throw;
						}
					}
					swWrite.Stop();
					ProfileRecord("Export.WriteTiles", swWrite.ElapsedTicks);
					Console.WriteLine($"Write phase completed for z{zoom} in {swWrite.ElapsedMilliseconds} ms.");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[ExportError] z{zoom} failed: {ex}");
					throw;
				}
			}

			CreateCoverageJson(outputDirectory);
			Console.WriteLine("[Diagnostics] Write-phase summary:");
			foreach (var kv in writeDiagnostics.OrderBy(kv => kv.Key))
			{
				Console.WriteLine($"  {kv.Key}: {kv.Value}");
			}
			Console.WriteLine($"  totalFeaturesWritten: {totalFeaturesWritten}");
			if (writeSkipSamples.Count > 0)
			{
				Console.WriteLine($"[Diagnostics] Write-skip sample count: {writeSkipSamples.Count}");
				foreach (var s in writeSkipSamples.Take(20)) Console.WriteLine("  " + s);
			}
			if (traceFailSamples.Count > 0)
			{
				Console.WriteLine($"[Diagnostics] Settlement-build sample count (write-phase): {traceFailSamples.Count}");
				foreach (var s in traceFailSamples.Take(20)) Console.WriteLine("  " + s);
			}

			PrintProfiles();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[ExportError] Export failed: {ex}");
			throw;
		}
	}

	public async Task DryRunReport()
	{
		Console.WriteLine($"[DryRun] Reading object source file: {sourceFile}");
		List<MapObject> objects = ObjectSourceFileReader.ReadObjects(sourceFile);
		Console.WriteLine($"[DryRun] Loaded {objects.Count} objects.");

		var swFilter = Stopwatch.StartNew();
		var buildingsBag = new ConcurrentBag<BuildingCandidate>();
		int rejectedNoFootprint = 0;
		int rejectedByType = 0;
		int rejectedByArea = 0;
		int rejectedBySide = 0;

		// Sequential pass to avoid parallel enumerator crashes
		foreach (var obj in objects)
		{
			if (obj.Footprint is not { Count: >= 3 } footprint)
			{
				Interlocked.Increment(ref rejectedNoFootprint);
				continue;
			}

			string typeName = obj.TypeName?.Trim() ?? string.Empty;
			bool excluded = false;
			foreach (string token in ExcludedTypeTokens)
			{
				if (typeName.Contains(token, StringComparison.OrdinalIgnoreCase)) { Interlocked.Increment(ref rejectedByType); excluded = true; break; }
			}
			if (excluded) continue;

			double area = Math.Abs(SignedArea(footprint));
			if (area < MinBuildingAreaSqm) { Interlocked.Increment(ref rejectedByArea); continue; }

			// Avoid LINQ Min/Max (can trigger a CLR enumerator bug under heavy parallelism)
			double minX = double.MaxValue;
			double maxX = double.MinValue;
			double minZ = double.MaxValue;
			double maxZ = double.MinValue;
			for (int pi = 0; pi < footprint.Count; pi++)
			{
				var p = footprint[pi];
				if (p.X < minX) minX = p.X;
				if (p.X > maxX) maxX = p.X;
				if (p.Z < minZ) minZ = p.Z;
				if (p.Z > maxZ) maxZ = p.Z;
			}
			double minSide = Math.Min(maxX - minX, maxZ - minZ);
			if (minSide < MinBuildingMinSideMeters) { Interlocked.Increment(ref rejectedBySide); continue; }

			buildingsBag.Add(new BuildingCandidate(obj, [.. footprint]));
		}

		List<BuildingCandidate> buildings = buildingsBag.ToList();

		swFilter.Stop();
		ProfileRecord("DryRun.CandidateFilter", swFilter.ElapsedTicks);
		Console.WriteLine($"[DryRun] Candidate buildings: {buildings.Count} (rejected: footprint={rejectedNoFootprint}, type={rejectedByType}, area={rejectedByArea}, side={rejectedBySide})");

		if (buildings.Count == 0)
		{
			return;
		}

		// Use the geometry-first BuildSettlements path for dry-run.
		var swCCL = Stopwatch.StartNew();
		var buildResult = BuildSettlements(objects);
		swCCL.Stop();
		Console.WriteLine($"[DryRun] Built {buildResult.Settlements.Count} settlement clusters (geometry-union).");
		double coverPctDry = buildResult.TotalCandidateCount > 0 ? (100.0 * buildResult.UsedBuildingCount / (double)buildResult.TotalCandidateCount) : 0.0;
		Console.WriteLine($"[DryRun] Buildings covered: {buildResult.UsedBuildingCount}/{buildResult.TotalCandidateCount} ({coverPctDry:F2}%)");

		ProfileRecord("DryRun.Total", swFilter.ElapsedTicks + swCCL.ElapsedTicks);
		PrintProfiles();
		await Task.CompletedTask;

        
	}

	private BuildResult BuildSettlements(List<MapObject> objects)
	{
		var buildingsBag = new ConcurrentBag<BuildingCandidate>();

		// Use sequential loop here to avoid CLR internal enumerator issues under heavy parallelism.
		foreach (var obj in objects)
		{
			if (!TryCreateBuildingCandidate(obj, out BuildingCandidate? building)) continue;
			buildingsBag.Add(building!);
		}

		List<BuildingCandidate> buildings = buildingsBag.ToList();

		Console.WriteLine($"Selected {buildings.Count} building candidates for settlement clustering.");
		ProfileRecord("BuildSettlements.CandidateCount", buildings.Count);

		if (buildings.Count == 0)
		{
			return new BuildResult(new List<SettlementCandidate>(), 0, 0);
		}

		var swGrouping = Stopwatch.StartNew();
		List<SettlementGeometryCandidate> components = BuildSettlementGeometryCandidates(buildings);
		swGrouping.Stop();
		ProfileRecord("Geometry.Grouping", swGrouping.ElapsedTicks);

		List<SettlementCandidate> settlements = new();
		var usedBuildingIds = new HashSet<int>();
		int tooFewBuildings = 0;
		int hullBuildFail = 0;
		int hullTooFewPoints = 0;
		int areaTooSmall = 0;
		const int SampleLimit = 200;
		var acceptedBuildingCounts = new List<int>();
		foreach (var comp in components)
		{
			int buildingCount = comp.BuildingIds.Count;
			if (buildingCount < MinBuildingsPerSettlement)
			{
				tooFewBuildings++;
				if (traceFailSamples.Count < SampleLimit)
				{
					var envelope = comp.Envelope;
					int firstId = comp.BuildingIds.FirstOrDefault();
					traceFailSamples.Add($"tooFewBuildings,{buildingCount},{envelope.MinX:F0},{envelope.MinY:F0},{envelope.MaxX:F0},{envelope.MaxY:F0},{firstId}");
				}
				continue;
			}
			if (comp.Geometry is null)
			{
				hullBuildFail++;
				if (traceFailSamples.Count < SampleLimit)
				{
					var envelope = comp.Envelope;
					int firstId = comp.BuildingIds.FirstOrDefault();
					traceFailSamples.Add($"hullBuildFail,{buildingCount},{envelope.MinX:F0},{envelope.MinY:F0},{envelope.MaxX:F0},{envelope.MaxY:F0},{firstId}");
				}
				continue;
			}

			List<DcsPoint>? hull = TryConvertSettlementPolygonToRing(comp.Geometry);
			if (hull is null)
			{
				hullBuildFail++;
				if (traceFailSamples.Count < SampleLimit)
				{
					var envelope = comp.Envelope;
					int firstId = comp.BuildingIds.FirstOrDefault();
					traceFailSamples.Add($"hullBuildFail,{buildingCount},{envelope.MinX:F0},{envelope.MinY:F0},{envelope.MaxX:F0},{envelope.MaxY:F0},{firstId}");
				}
				continue;
			}
			if (hull.Count < 4)
			{
				hullTooFewPoints++;
				if (traceFailSamples.Count < SampleLimit) traceFailSamples.Add($"hullTooFewPoints,{buildingCount},{hull.Count}");
				continue;
			}
			double settlementArea = comp.Geometry.Area;
			if (settlementArea < MinSettlementAreaSqm)
			{
				areaTooSmall++;
				if (traceFailSamples.Count < SampleLimit) traceFailSamples.Add($"areaTooSmall,{buildingCount},{settlementArea}");
				continue;
			}
			hull = EnsureClosedRing(hull);
			if (hull.Count < 4)
			{
				hullTooFewPoints++;
				if (traceFailSamples.Count < SampleLimit) traceFailSamples.Add($"postEnsureTooFew,{buildingCount},{hull.Count}");
				continue;
			}
			// record used building ids for coverage reporting
			foreach (int id in comp.BuildingIds) usedBuildingIds.Add(id);
			acceptedBuildingCounts.Add(buildingCount);
			settlements.Add(new SettlementCandidate(hull, buildingCount));
		}

		// Produce console-only diagnostics for BuildSettlements (no files)
		var top = acceptedBuildingCounts.OrderByDescending(n => n).Take(50).ToArray();
		Console.WriteLine("[Diagnostics] BuildSettlements summary:");
		Console.WriteLine($"  TotalComponents:{components.Count}");
		Console.WriteLine($"  AcceptedSettlements:{settlements.Count}");
		Console.WriteLine($"  UsedBuildingIds:{usedBuildingIds.Count}");
		Console.WriteLine($"  TooFewBuildings:{tooFewBuildings}");
		Console.WriteLine($"  HullBuildFail:{hullBuildFail}");
		Console.WriteLine($"  HullTooFewPoints:{hullTooFewPoints}");
		Console.WriteLine($"  AreaTooSmall:{areaTooSmall}");
		Console.WriteLine($"  TopAcceptedBuildingCounts:" + string.Join(',', top));
		if (traceFailSamples.Count > 0)
		{
			Console.WriteLine($"[Diagnostics] Settlement-build sample count: {traceFailSamples.Count}");
			foreach (var s in traceFailSamples.Take(10)) Console.WriteLine("  " + s);
		}

		Console.WriteLine($"Built {settlements.Count} settlement clusters after geometry union.");
		return new BuildResult(settlements, buildings.Count, usedBuildingIds.Count);
	}

	private static List<SettlementGeometryCandidate> BuildSettlementGeometryCandidates(IReadOnlyList<BuildingCandidate> buildings)
	{
		double dilationDistance = HullDilationCells * OccupancyCellSizeMeters;
		Stopwatch bufferStopwatch = Stopwatch.StartNew();
		Stopwatch progressStopwatch = Stopwatch.StartNew();
		var bufferedBuildings = new List<BufferedBuilding>(buildings.Count);

		for (int buildingId = 0; buildingId < buildings.Count; buildingId++)
		{
			Polygon? polygon = TryCreateLocalPolygon(buildings[buildingId].Footprint);
			if (polygon is null || polygon.IsEmpty)
			{
				continue;
			}

			Geometry buffered = polygon.Buffer(dilationDistance, 1);
			if (buffered.IsEmpty)
			{
				continue;
			}

			bufferedBuildings.Add(new BufferedBuilding(buildingId, buffered));

			if (progressStopwatch.ElapsedMilliseconds >= 5000)
			{
				Console.WriteLine($"[Progress] Buffered {buildingId + 1}/{buildings.Count} building footprints; kept {bufferedBuildings.Count} buffered geometries...");
				progressStopwatch.Restart();
			}
		}

		if (bufferedBuildings.Count == 0)
		{
			return [];
		}
		bufferStopwatch.Stop();
		ProfileRecord("Geometry.Grouping.BufferBuildings", bufferStopwatch.ElapsedTicks);
		Console.WriteLine($"[Progress] Buffered {bufferedBuildings.Count} building geometries in {bufferStopwatch.ElapsedMilliseconds} ms. Building spatial index...");

		Stopwatch indexStopwatch = Stopwatch.StartNew();
		STRtree<BufferedBuilding> spatialIndex = new();
		foreach (BufferedBuilding building in bufferedBuildings)
		{
			spatialIndex.Insert(building.BufferedGeometry.EnvelopeInternal, building);
		}
		spatialIndex.Build();
		indexStopwatch.Stop();
		ProfileRecord("Geometry.Grouping.BuildIndex", indexStopwatch.ElapsedTicks);
		Console.WriteLine($"[Progress] Spatial index built in {indexStopwatch.ElapsedMilliseconds} ms. Finding connected settlement groups...");

		Stopwatch componentStopwatch = Stopwatch.StartNew();
		progressStopwatch.Restart();
		HashSet<int> visited = [];
		var settlements = new List<SettlementGeometryCandidate>();
		int componentsProcessed = 0;
		foreach (BufferedBuilding seed in bufferedBuildings.OrderBy(building => building.BuildingId))
		{
			if (!visited.Add(seed.BuildingId))
			{
				continue;
			}

			Queue<BufferedBuilding> pending = new();
			pending.Enqueue(seed);
			var componentBuildings = new List<BufferedBuilding>();
			var componentGeometries = new List<Geometry>();
			HashSet<int> componentBuildingIds = [seed.BuildingId];
			Envelope componentEnvelope = new(seed.BufferedGeometry.EnvelopeInternal);

			while (pending.Count > 0)
			{
				BufferedBuilding current = pending.Dequeue();
				componentBuildings.Add(current);
				componentGeometries.Add(current.BufferedGeometry);
				componentEnvelope.ExpandToInclude(current.BufferedGeometry.EnvelopeInternal);

				foreach (BufferedBuilding candidate in spatialIndex.Query(current.BufferedGeometry.EnvelopeInternal))
				{
					if (visited.Contains(candidate.BuildingId))
					{
						continue;
					}

					if (!current.BufferedGeometry.Intersects(candidate.BufferedGeometry))
					{
						continue;
					}

					visited.Add(candidate.BuildingId);
					componentBuildingIds.Add(candidate.BuildingId);
					pending.Enqueue(candidate);
				}
			}

			if (componentBuildingIds.Count < MinBuildingsPerSettlement)
			{
				settlements.Add(new SettlementGeometryCandidate(null, componentBuildingIds, componentEnvelope));
				componentsProcessed++;
				if (progressStopwatch.ElapsedMilliseconds >= 5000)
				{
					Console.WriteLine($"[Progress] Processed {componentsProcessed} settlement groups; assigned {visited.Count}/{bufferedBuildings.Count} buffered buildings; built {settlements.Count} settlement candidates...");
					progressStopwatch.Restart();
				}
				continue;
			}

			Geometry unioned = componentGeometries.Count == 1
				? componentGeometries[0]
				: UnaryUnionOp.Union(componentGeometries);
			if (unioned.IsEmpty)
			{
				continue;
			}

			var polygons = new List<Polygon>();
			CollectPolygons(unioned, polygons);
			Polygon? polygon = polygons
				.OrderByDescending(candidate => candidate.Area)
				.ThenBy(candidate => candidate.EnvelopeInternal.MinX)
				.ThenBy(candidate => candidate.EnvelopeInternal.MinY)
				.FirstOrDefault();
			if (polygon is not null)
			{
				settlements.Add(new SettlementGeometryCandidate(polygon, componentBuildingIds, componentEnvelope));
			}

			componentsProcessed++;
			if (progressStopwatch.ElapsedMilliseconds >= 5000)
			{
				Console.WriteLine($"[Progress] Processed {componentsProcessed} settlement groups; assigned {visited.Count}/{bufferedBuildings.Count} buffered buildings; built {settlements.Count} settlement polygons...");
				progressStopwatch.Restart();
			}
		}
		componentStopwatch.Stop();
		ProfileRecord("Geometry.Grouping.UnionComponents", componentStopwatch.ElapsedTicks);
		Console.WriteLine($"[Progress] Settlement grouping finished in {componentStopwatch.ElapsedMilliseconds} ms with {settlements.Count} polygons.");

		return settlements;
	}

	private static List<DcsPoint>? TryConvertSettlementPolygonToRing(Polygon polygon)
	{
		List<DcsPoint> ring = [];
		foreach (NtsCoordinate coordinate in polygon.ExteriorRing.Coordinates)
		{
			if (ring.Count > 0)
			{
				DcsPoint last = ring[^1];
				if (last.X == coordinate.X && last.Z == coordinate.Y)
				{
					continue;
				}
			}

			ring.Add(new DcsPoint { X = coordinate.X, Z = coordinate.Y });
		}

		ring = EnsureClosedRing(ring);
		return ring.Count < 4 ? null : ring;
	}

	private sealed record BuildResult(List<SettlementCandidate> Settlements, int TotalCandidateCount, int UsedBuildingCount);

	private static Polygon? TryCreateLocalPolygon(IReadOnlyList<DcsPoint> footprint)
	{
		if (footprint.Count < 3)
		{
			return null;
		}

		var coordinates = new List<NtsCoordinate>(footprint.Count + 1);
		foreach (DcsPoint point in footprint)
		{
			if (coordinates.Count == 0 || coordinates[^1].X != point.X || coordinates[^1].Y != point.Z)
			{
				coordinates.Add(new NtsCoordinate(point.X, point.Z));
			}
		}

		if (coordinates.Count < 3)
		{
			return null;
		}

		if (!coordinates[0].Equals2D(coordinates[^1]))
		{
			coordinates.Add(new NtsCoordinate(coordinates[0]));
		}

		if (coordinates.Count < 4)
		{
			return null;
		}

		try
		{
			LinearRing shell = GeometryFactory.CreateLinearRing(coordinates.ToArray());
			Polygon polygon = GeometryFactory.CreatePolygon(shell);
			if (polygon.IsValid)
			{
				return polygon;
			}

			Geometry fixedGeometry = GeometryFixer.Fix(polygon);
			if (fixedGeometry.IsEmpty)
			{
				return null;
			}

			if (fixedGeometry is Polygon fixedPolygon)
			{
				return fixedPolygon;
			}

			var polygons = new List<Polygon>();
			CollectPolygons(fixedGeometry, polygons);
			return polygons.OrderByDescending(candidate => candidate.Area).FirstOrDefault();
		}
		catch
		{
			return null;
		}
	}

	private static List<DcsPoint> EnsureClosedRing(List<DcsPoint> ring)
	{
		if (ring.Count == 0)
		{
			return ring;
		}

		List<DcsPoint> closed = [.. ring];
		DcsPoint first = closed[0];
		DcsPoint last = closed[^1];
		if (first.X != last.X || first.Z != last.Z)
		{
			closed.Add(new DcsPoint { X = first.X, Z = first.Z });
		}

		return closed;
	}

	private bool TryCreateBuildingCandidate(MapObject obj, out BuildingCandidate? building)
	{
		building = null;

		if (obj.Footprint is not { Count: >= 3 } footprint)
		{
			return false;
		}

		string typeName = obj.TypeName?.Trim() ?? string.Empty;
		if (typeName.Length == 0)
		{
			return false;
		}

		foreach (string token in ExcludedTypeTokens)
		{
			if (typeName.Contains(token, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		double area = Math.Abs(SignedArea(footprint));
		if (area < MinBuildingAreaSqm)
		{
			return false;
		}

		double minX = footprint.Min(point => point.X);
		double maxX = footprint.Max(point => point.X);
		double minZ = footprint.Min(point => point.Z);
		double maxZ = footprint.Max(point => point.Z);
		double minSide = Math.Min(maxX - minX, maxZ - minZ);

		if (minSide < MinBuildingMinSideMeters)
		{
			return false;
		}

		building = new BuildingCandidate(obj, [.. footprint]);
		return true;
	}


	private async Task WriteTile(int z, int x, int y, TileData tileData)
	{
		VectorTileLayer layer = new("settlements", 2, TileExtent);

		foreach (SettlementTileData settlement in tileData.Settlements)
		{
			// Targeted diagnostics for a user-specified tile.
			if (z == 14 && x == 10123 && y == 6069)
			{
				try
				{
					Console.WriteLine($"[TileDiag] Settlement buildings={settlement.BuildingCount}");
					if (settlement.OriginalHull is not null)
					{
						var orig = settlement.OriginalHull;
						Console.WriteLine($"  original hull pts={orig.Count} area={Math.Abs(SignedArea(orig)):F2}");
					}
					Console.WriteLine($"  tile geometry rings={settlement.TileGeometry.Count} points={settlement.TileGeometry.Sum(r => r.Count)}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[TileDiag] exception: {ex.Message}");
				}
			}
			List<ArraySegment<Coordinate>> geometry = settlement.TileGeometry.Select(r => new ArraySegment<Coordinate>(r.ToArray())).ToList();
			VectorTileFeature feature = new(
				Interlocked.Increment(ref settlementId).ToString(),
				geometry,
				[new("Buildings", settlement.BuildingCount)],
				Tile.GeomType.Polygon,
				TileExtent);

			layer.VectorTileFeatures.Add(feature);
			writeDiagnostics.AddOrUpdate("features.added", 1, (_, v) => v + 1);
			Interlocked.Increment(ref totalFeaturesWritten);
		}

		string outputPath = Path.Combine(outputDirectory, z.ToString(), x.ToString(), $"{y}.mvt");
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
		using (FileStream fs = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			VectorTileEncoder.Encode([layer], fs);
		}

		if (layer.VectorTileFeatures.Count == 0)
		{
			writeDiagnostics.AddOrUpdate("tile.empty", 1, (_, v) => v + 1);
			// Sample NTS-derived geometry state for empty tiles.
			bool isTargetTile = (z == 14 && x == 10123 && y == 6069);
			const int LargeAssignedThreshold = 50;
			if (isTargetTile || tileData.Settlements.Count >= LargeAssignedThreshold)
			{
				int sampleLimit = isTargetTile ? 200 : 5;
				Console.WriteLine($"[Diagnostics] Empty tile z{z}/{x}/{y} assigned {tileData.Settlements.Count} settlements; sampling up to {sampleLimit}:");
				int idx = 0;
				foreach (var s in tileData.Settlements)
				{
					if (idx++ >= sampleLimit) break;
					try
					{
						Console.WriteLine($"  settlement[{idx}] buildings={s.BuildingCount} rings={s.TileGeometry.Count} points={s.TileGeometry.Sum(r => r.Count)}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"  settlement[{idx}] diagnostics failed: {ex.Message}");
					}
				}
			}
		}
		else
		{
			writeDiagnostics.AddOrUpdate("tile.nonempty", 1, (_, v) => v + 1);
		}

		if (!validatedSampleTile && layer.VectorTileFeatures.Count > 0)
		{
			validatedSampleTile = true;
			ValidateWrittenTile(outputPath, TileExtent);
		}

		await Task.CompletedTask;
	}

	private List<LatLong> ProjectFootprint(IReadOnlyCollection<DcsPoint> footprint)
	{
		var list = new List<LatLong>(footprint.Count);
		foreach (var point in footprint)
		{
			var ll = _2dTileExporter.Utils.CoordConversion.SafeLOtoLL(coordConverter, new DcsCoord { X = point.X, Y = point.Z });
			list.Add(ll);
		}
		return list;
	}

	private static List<TilePoint> ProjectFootprintToTileSpace(List<LatLong> footprint, TileData tileData, uint extent)
	{
		return [.. footprint.Select(point => new TilePoint(
			QuantizeTileCoordinate(((point.Lon - tileData.MinLon) / (tileData.MaxLon - tileData.MinLon)) * extent),
			QuantizeTileCoordinate(((tileData.MaxLat - point.Lat) / (tileData.MaxLat - tileData.MinLat)) * extent)))];
	}

	private static bool TryBuildTileGeometry(List<LatLong> footprint, TileData tileData, uint extent, out List<List<Coordinate>>? tileGeometry)
	{
		tileGeometry = null;

		var projectedFull = ProjectFootprintToTileSpace(footprint, tileData, extent);
		Polygon? footprintPolygon = TryCreatePolygon(projectedFull);
		if (footprintPolygon is null || footprintPolygon.IsEmpty)
		{
			return false;
		}

		Polygon tilePolygon = CreateTilePolygon(extent);
		if (!TryIntersectTileGeometry(footprintPolygon, tilePolygon, out Geometry? clipped))
		{
			return false;
		}
		if (clipped is null)
		{
			return false;
		}

		if (clipped.IsEmpty)
		{
			return false;
		}

		var polygons = new List<Polygon>();
		CollectPolygons(clipped, polygons);
		if (polygons.Count == 0)
		{
			return false;
		}

		tileGeometry = new List<List<Coordinate>>();
		foreach (Polygon polygon in polygons)
		{
			var ring = polygon.ExteriorRing.Coordinates
				.Select(c => new Coordinate(
					(int)Math.Clamp((int)Math.Round(c.X), 0, (int)extent),
					(int)Math.Clamp((int)Math.Round(c.Y), 0, (int)extent)))
				.ToList();

			var fixedRing = ValidateAndFixRing(ring);
			if (fixedRing is not null)
			{
				tileGeometry.Add(fixedRing);
			}
		}

		return tileGeometry.Count > 0;
	}

	private static bool TryIntersectTileGeometry(Geometry footprintPolygon, Polygon tilePolygon, out Geometry? clipped)
	{
		clipped = null;

		try
		{
			clipped = OverlayNGRobust.Overlay(footprintPolygon, tilePolygon, SpatialFunction.Intersection);
			return true;
		}
		catch (TopologyException)
		{
			Geometry fixedGeometry = GeometryFixer.Fix(footprintPolygon);
			if (fixedGeometry.IsEmpty)
			{
				return false;
			}

			clipped = OverlayNGRobust.Overlay(fixedGeometry, tilePolygon, SpatialFunction.Intersection);
			return true;
		}
	}

	private static double QuantizeTileCoordinate(double value)
	{
		return Math.Round(value, MidpointRounding.AwayFromZero);
	}

	private static Polygon? TryCreatePolygon(List<TilePoint> ring)
	{
		if (ring.Count < 3)
		{
			return null;
		}

		var coordinates = new List<NtsCoordinate>(ring.Count + 1);
		for (int i = 0; i < ring.Count; i++)
		{
			TilePoint point = ring[i];
			if (coordinates.Count == 0 || coordinates[^1].X != point.X || coordinates[^1].Y != point.Y)
			{
				coordinates.Add(new NtsCoordinate(point.X, point.Y));
			}
		}

		if (coordinates.Count < 3)
		{
			return null;
		}

		if (!coordinates[0].Equals2D(coordinates[^1]))
		{
			coordinates.Add(new NtsCoordinate(coordinates[0]));
		}

		if (coordinates.Count < 4)
		{
			return null;
		}

		try
		{
			LinearRing shell = GeometryFactory.CreateLinearRing(coordinates.ToArray());
			Polygon polygon = GeometryFactory.CreatePolygon(shell);
			if (polygon.IsValid)
			{
				return polygon;
			}

			Geometry fixedGeometry = GeometryFixer.Fix(polygon);
			if (fixedGeometry.IsEmpty)
			{
				return null;
			}

			if (fixedGeometry is Polygon fixedPolygon)
			{
				return fixedPolygon;
			}

			var polygons = new List<Polygon>();
			CollectPolygons(fixedGeometry, polygons);
			return polygons.OrderByDescending(candidate => candidate.Area).FirstOrDefault();
		}
		catch
		{
			return null;
		}
	}

	private static Polygon CreateTilePolygon(uint extent)
	{
		NtsCoordinate[] coordinates =
		[
			new NtsCoordinate(0, 0),
			new NtsCoordinate(extent, 0),
			new NtsCoordinate(extent, extent),
			new NtsCoordinate(0, extent),
			new NtsCoordinate(0, 0)
		];
		return GeometryFactory.CreatePolygon(coordinates);
	}

	private static void CollectPolygons(Geometry geometry, List<Polygon> polygons)
	{
		switch (geometry)
		{
			case Polygon polygon:
				polygons.Add(polygon);
				break;
			case MultiPolygon multiPolygon:
				for (int i = 0; i < multiPolygon.NumGeometries; i++)
				{
					CollectPolygons(multiPolygon.GetGeometryN(i), polygons);
				}
				break;
			case GeometryCollection collection:
				for (int i = 0; i < collection.NumGeometries; i++)
				{
					CollectPolygons(collection.GetGeometryN(i), polygons);
				}
				break;
		}
	}

	private static List<Coordinate>? ValidateAndFixRing(List<Coordinate> ring)
	{
		if (ring.Count < 3)
		{
			return null;
		}

		List<Coordinate> cleaned = [];
		foreach (Coordinate point in ring)
		{
			if (cleaned.Count == 0)
			{
				cleaned.Add(point);
				continue;
			}

			Coordinate last = cleaned[^1];
			if (last.X != point.X || last.Y != point.Y)
			{
				cleaned.Add(point);
			}
		}

		if (cleaned.Count < 3)
		{
			return null;
		}

		Coordinate first = cleaned[0];
		Coordinate lastPoint = cleaned[^1];
		if (first.X != lastPoint.X || first.Y != lastPoint.Y)
		{
			cleaned.Add(new Coordinate(first.X, first.Y));
		}

		if (cleaned.Count < 4)
		{
			return null;
		}

		double area = SignedArea(cleaned);
		if (area == 0)
		{
			return null;
		}

		if (area < 0)
		{
			cleaned.Reverse();
			first = cleaned[0];
			lastPoint = cleaned[^1];
			if (first.X != lastPoint.X || first.Y != lastPoint.Y)
			{
				cleaned.Add(new Coordinate(first.X, first.Y));
			}
		}

		return cleaned;
	}

	private static void ValidateWrittenTile(string outputPath, uint extent)
	{
		using FileStream fileStream = File.OpenRead(outputPath);
		List<VectorTileLayer> layers = VectorTileParser.Parse(fileStream);

		foreach (VectorTileLayer layer in layers)
		{
			foreach (VectorTileFeature feature in layer.VectorTileFeatures.Where(feature => feature.GeometryType == Tile.GeomType.Polygon))
			{
				foreach (ArraySegment<Coordinate> ringSegment in feature.Geometry)
				{
					Coordinate[] coords = ringSegment.ToArray();
					if (coords.Length < 4)
					{
						throw new InvalidDataException($"Polygon feature in '{outputPath}' has too few points.");
					}

					Coordinate first = coords[0];
					Coordinate last = coords[^1];
					if (first.X != last.X || first.Y != last.Y)
					{
						throw new InvalidDataException($"Polygon feature in '{outputPath}' is not closed.");
					}

					if (SignedArea(coords.ToList()) == 0)
					{
						throw new InvalidDataException($"Polygon feature in '{outputPath}' has zero area.");
					}

					foreach (Coordinate coord in coords)
					{
						if (coord.X < 0 || coord.X > extent || coord.Y < 0 || coord.Y > extent)
						{
							throw new InvalidDataException($"Polygon feature in '{outputPath}' has coordinates outside tile extent.");
						}
					}
				}
			}
		}
	}

	private static void CreateCoverageJson(string outputPath)
	{
		Dictionary<string, List<string>> coverage = new();

		foreach (string file in Directory.EnumerateFiles(outputPath, "*.mvt", SearchOption.AllDirectories))
		{
			string[] parts = file.Split(Path.DirectorySeparatorChar);
			int z = int.Parse(parts[^3]);
			int x = int.Parse(parts[^2]);
			int y = int.Parse(Path.GetFileNameWithoutExtension(parts[^1]));

			if (!coverage.TryGetValue(z.ToString(), out List<string>? tiles))
			{
				tiles = [];
				coverage[z.ToString()] = tiles;
			}

			tiles.Add($"{x}:{y}");
		}

		string json = JsonSerializer.Serialize(coverage, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(Path.Combine(outputPath, "coverage.json"), json);
	}

	private static Bounds GetBounds(List<LatLong> footprint)
	{
		return new Bounds(
			footprint.Min(point => point.Lon),
			footprint.Min(point => point.Lat),
			footprint.Max(point => point.Lon),
			footprint.Max(point => point.Lat));
	}

	private static HashSet<(int tileX, int tileY)> GetIntersectingTiles(Bounds bounds, int zoom)
	{
		HashSet<(int tileX, int tileY)> tiles = [];
		int maxIndex = (1 << zoom) - 1;

		// Expand geographic bounds slightly to be tolerant of projection and numeric edge cases
		double expandDeg = 1e-6; // ~0.1m at equator, conservative margin
		double minLon = bounds.MinLon - expandDeg;
		double maxLon = bounds.MaxLon + expandDeg;
		double minLat = bounds.MinLat - expandDeg;
		double maxLat = bounds.MaxLat + expandDeg;

		int minTileX = Math.Clamp(LonToTileX(minLon, zoom), 0, maxIndex);
		int maxTileX = Math.Clamp(LonToTileX(maxLon, zoom), 0, maxIndex);
		int topTileY = Math.Clamp(LatToTileY(maxLat, zoom), 0, maxIndex);
		int bottomTileY = Math.Clamp(LatToTileY(minLat, zoom), 0, maxIndex);

		for (int tileX = Math.Min(minTileX, maxTileX); tileX <= Math.Max(minTileX, maxTileX); tileX++)
		{
			for (int tileY = Math.Min(topTileY, bottomTileY); tileY <= Math.Max(topTileY, bottomTileY); tileY++)
			{
				Bounds tileBounds = new(
					TileXToLon(tileX, zoom),
					TileYToLat(tileY + 1, zoom),
					TileXToLon(tileX + 1, zoom),
					TileYToLat(tileY, zoom));

				if (BoundsIntersect(bounds, tileBounds))
				{
					tiles.Add((tileX, tileY));
				}
			}
		}

		return tiles;
	}

	private static bool BoundsIntersect(Bounds left, Bounds right)
	{
		return left.MinLon <= right.MaxLon
			&& left.MaxLon >= right.MinLon
			&& left.MinLat <= right.MaxLat
			&& left.MaxLat >= right.MinLat;
	}

	private static int LonToTileX(double lonDeg, int z)
	{
		double n = 1 << z;
		return (int)Math.Floor((lonDeg + 180.0) / 360.0 * n);
	}

	private static int LatToTileY(double latDeg, int z)
	{
		double latRad = latDeg * Math.PI / 180.0;
		double n = 1 << z;
		double y = (1.0 - Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n;
		return (int)Math.Floor(y);
	}

	private static double TileXToLon(int x, int z)
	{
		double n = 1 << z;
		return (x / n) * 360.0 - 180.0;
	}

	private static double TileYToLat(int y, int z)
	{
		double n = 1 << z;
		double mercY = Math.PI * (1.0 - (2.0 * y / n));
		return Math.Atan(Math.Sinh(mercY)) * 180.0 / Math.PI;
	}

	private static double SignedArea(IReadOnlyList<DcsPoint> ring)
	{
		double area = 0;
		for (int index = 0; index < ring.Count - 1; index++)
		{
			DcsPoint a = ring[index];
			DcsPoint b = ring[index + 1];
			area += (a.X * b.Z) - (b.X * a.Z);
		}

		return area / 2.0;
	}

	private static double SignedArea(IReadOnlyList<Coordinate> ring)
	{
		double area = 0;
		for (int index = 0; index < ring.Count - 1; index++)
		{
			Coordinate a = ring[index];
			Coordinate b = ring[index + 1];
			area += (a.X * b.Y) - (b.X * a.Y);
		}

		return area / 2.0;
	}

	private sealed class TileData
	{
		public required double MinLat { get; init; }
		public required double MinLon { get; init; }
		public required double MaxLat { get; init; }
		public required double MaxLon { get; init; }
		public List<SettlementTileData> Settlements { get; } = new();
	}

	private sealed class SettlementTileData
	{
		public required int BuildingCount { get; init; }
		public required List<LatLong> Footprint { get; init; }
		public required List<List<Coordinate>> TileGeometry { get; init; }
		// Original DCS-space hull (meters) for diagnostics
		public List<DcsPoint>? OriginalHull { get; init; }
	}

	private sealed class BuildingCandidate(MapObject source, IReadOnlyList<DcsPoint> footprint)
	{
		public MapObject Source { get; } = source;
		public IReadOnlyList<DcsPoint> Footprint { get; } = footprint;
	}

	private sealed class BufferedBuilding(int buildingId, Geometry bufferedGeometry)
	{
		public int BuildingId { get; } = buildingId;
		public Geometry BufferedGeometry { get; } = bufferedGeometry;
	}

	private sealed class SettlementGeometryCandidate(Polygon? geometry, HashSet<int> buildingIds, Envelope envelope)
	{
		public Polygon? Geometry { get; } = geometry;
		public HashSet<int> BuildingIds { get; } = buildingIds;
		public Envelope Envelope { get; } = envelope;
	}

	private sealed class SettlementCandidate(List<DcsPoint> footprint, int buildingCount)
	{
		public List<DcsPoint> Footprint { get; } = footprint;
		public int BuildingCount { get; } = buildingCount;
	}

	private readonly record struct Bounds(double MinLon, double MinLat, double MaxLon, double MaxLat);
	private readonly record struct TilePoint(double X, double Y);

	private static List<TilePoint> ComputeTileIntersectionPoints(List<TilePoint> projected, uint extent)
	{
		if (projected == null || projected.Count == 0) return new List<TilePoint>();
		var pts = new List<TilePoint>();
		// include points inside tile
		foreach (var p in projected)
		{
			if (p.X >= 0 && p.X <= extent && p.Y >= 0 && p.Y <= extent)
			{
				pts.Add(p);
			}
		}

		// for each edge, compute intersections with the four tile edges
		for (int i = 0; i < projected.Count; i++)
		{
			var a = projected[i];
			var b = projected[(i + 1) % projected.Count];
			// vertical x=0
			if (b.X != a.X)
			{
				double t = (0 - a.X) / (b.X - a.X);
				if (t >= 0 && t <= 1)
				{
					double y = a.Y + (b.Y - a.Y) * t;
					if (y >= -1e-6 && y <= extent + 1e-6) pts.Add(new TilePoint(0, y));
				}
				t = (extent - a.X) / (b.X - a.X);
				if (t >= 0 && t <= 1)
				{
					double y = a.Y + (b.Y - a.Y) * t;
					if (y >= -1e-6 && y <= extent + 1e-6) pts.Add(new TilePoint(extent, y));
				}
			}
			// horizontal y=0 and y=extent
			if (b.Y != a.Y)
			{
				double t = (0 - a.Y) / (b.Y - a.Y);
				if (t >= 0 && t <= 1)
				{
					double x = a.X + (b.X - a.X) * t;
					if (x >= -1e-6 && x <= extent + 1e-6) pts.Add(new TilePoint(x, 0));
				}
				t = (extent - a.Y) / (b.Y - a.Y);
				if (t >= 0 && t <= 1)
				{
					double x = a.X + (b.X - a.X) * t;
					if (x >= -1e-6 && x <= extent + 1e-6) pts.Add(new TilePoint(x, extent));
				}
			}
		}

		// deduplicate with tolerance
		var uniq = new List<TilePoint>();
		foreach (var p in pts)
		{
			bool found = false;
			foreach (var q in uniq)
			{
				if (Math.Abs(p.X - q.X) < 1e-6 && Math.Abs(p.Y - q.Y) < 1e-6) { found = true; break; }
			}
			if (!found) uniq.Add(p);
		}

		return uniq;
	}

}
