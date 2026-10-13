using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public sealed class SpawnSearchResult
    {
        public Vec3? Position { get; }
        public string Code { get; }
        public string Message { get; }
        public int CandidatesChecked { get; }
        internal SpawnSearchResult(Vec3? position, string code, string message, int candidatesChecked)
        { Position = position; Code = code; Message = message; CandidatesChecked = candidatesChecked; }
    }
    public static class EntityBuilder
    {
        public static IReadOnlyList<GoldSrcBrush> Enclosure(IReadOnlyList<Vec3> vertices, string texture, double t)
        {
            var lo = new Vec3(vertices.Min(p => p.X) - 128, vertices.Min(p => p.Y) - 128, vertices.Min(p => p.Z) - 128);
            var hi = new Vec3(vertices.Max(p => p.X) + 128, vertices.Max(p => p.Y) + 128, vertices.Max(p => p.Z) + 128);
            const double thickness = 16;
            var boxes = new[]{
    new Box(Frame.Identity,new Vec3(lo.X-thickness,lo.Y-thickness,lo.Z-thickness),new Vec3(hi.X+thickness,hi.Y+thickness,lo.Z)),
    new Box(Frame.Identity,new Vec3(lo.X-thickness,lo.Y-thickness,hi.Z),new Vec3(hi.X+thickness,hi.Y+thickness,hi.Z+thickness)),
    new Box(Frame.Identity,new Vec3(lo.X-thickness,lo.Y-thickness,lo.Z),new Vec3(lo.X,hi.Y+thickness,hi.Z)),
    new Box(Frame.Identity,new Vec3(hi.X,lo.Y-thickness,lo.Z),new Vec3(hi.X+thickness,hi.Y+thickness,hi.Z)),
    new Box(Frame.Identity,new Vec3(lo.X,lo.Y-thickness,lo.Z),new Vec3(hi.X,lo.Y,hi.Z)),
    new Box(Frame.Identity,new Vec3(lo.X,hi.Y,lo.Z),new Vec3(hi.X,hi.Y+thickness,hi.Z))
   };
            return boxes.Select(b => BrushBuilder.FromBox(b, texture, t)).ToList();
        }
        public static Vec3? FindSpawn(IReadOnlyList<GoldSrcBrush> floors, IReadOnlyList<GoldSrcBrush> obstacles, double t)
            => SearchSpawn(floors, obstacles, t).Position;
        public static SpawnSearchResult SearchSpawn(IReadOnlyList<GoldSrcBrush> floors, IReadOnlyList<GoldSrcBrush> obstacles, double t, int candidateLimit = 20000)
        {
            if (candidateLimit <= 0) throw new ArgumentOutOfRangeException(nameof(candidateLimit));
            if (!Vec3.Finite(t) || t <= 0) throw new ArgumentException("Tolerancia inválida.");
            int attempts = 0, compatibleFloors = 0;
            foreach (var floor in floors)
            {
                var verts = ConvexGeometryValidator.Validate(floor, t).Vertices;
                var top = floor.Faces.FirstOrDefault(f => f.Normal.Z > 0.999999); if (top == null) continue;
                compatibleFloors++;
                double z = top.Distance / top.Normal.Z;
                double minX = verts.Min(p => p.X) + 16.1, maxX = verts.Max(p => p.X) - 16.1;
                double minY = verts.Min(p => p.Y) + 16.1, maxY = verts.Max(p => p.Y) - 16.1;
                if (minX > maxX || minY > maxY) continue;
                var candidates = new List<Vec3> { new Vec3((minX + maxX) / 2, (minY + maxY) / 2, z) };
                double stepX = Math.Max(32, (maxX - minX) / 24), stepY = Math.Max(32, (maxY - minY) / 24);
                for (double x = minX; x <= maxX; x += stepX) for (double y = minY; y <= maxY; y += stepY) candidates.Add(new Vec3(x, y, z));
                foreach (var foot in candidates)
                {
                    if (attempts >= candidateLimit) return new SpawnSearchResult(null, "spawn_candidate_limit", "Se agotó el límite de " + candidateLimit + " candidatos antes de completar la búsqueda.", attempts);
                    attempts++;
                    if (floor.Faces.Any(f => f.Normal.Dot(foot) + Math.Abs(f.Normal.X) * 16 + Math.Abs(f.Normal.Y) * 16 > f.Distance + t)) continue;
                    var player = foot + new Vec3(0, 0, 37);
                    if (obstacles.All(b => !ConvexGeometryValidator.IntersectsPlayer(b, player))) return new SpawnSearchResult(player, "spawn_found", "Punto de inicio comprobado.", attempts);
                }
            }
            // Polygon decomposition can split a continuous floor into pieces narrower than the player.
            // Preserve the original search first; only then test support over the union of coplanar tops.
            var union=SearchUnionSupport(floors,obstacles,t,candidateLimit-attempts,ref attempts);
            if(union.HasValue)return new SpawnSearchResult(union,"spawn_found","Punto de inicio comprobado sobre la unión de pisos.",attempts);
            if (compatibleFloors == 0) return new SpawnSearchResult(null, "spawn_no_compatible_floor", "No hay pisos compatibles exportados ni suelo de recinto de prueba para colocar al jugador.", attempts);
            return new SpawnSearchResult(null, "spawn_insufficient_space", "Los pisos exportados no ofrecen un punto comprobado para el jugador (32 × 32 × 72 HU) entre los " + attempts + " candidatos examinados.", attempts);
        }
        private static Vec3? SearchUnionSupport(IReadOnlyList<GoldSrcBrush> floors,IReadOnlyList<GoldSrcBrush> obstacles,double t,int limit,ref int attempts)
        {
            var tops=new List<Tuple<double,List<Vec3>>>();
            foreach(var floor in floors)
            {
                var face=floor.Faces.FirstOrDefault(f=>f.Normal.Z>1-1e-8);if(face==null)continue;
                var points=ConvexGeometryValidator.Validate(floor,t).Vertices.Where(p=>Math.Abs(face.Normal.Dot(p)-face.Distance)<=t*2).ToList();
                double z=face.Distance/face.Normal.Z;var center=points.Aggregate(Vec3.Zero,(a,b)=>a+b)/points.Count;
                var polygon=points.OrderBy(p=>Math.Atan2(p.Y-center.Y,p.X-center.X)).Select(p=>new Vec3(p.X,p.Y,0)).ToList();tops.Add(Tuple.Create(z,polygon));
            }
            var groups=new List<List<Tuple<double,List<Vec3>>>>();foreach(var top in tops){var group=groups.FirstOrDefault(g=>Math.Abs(g[0].Item1-top.Item1)<=t);if(group==null){group=new List<Tuple<double,List<Vec3>>>();groups.Add(group);}group.Add(top);}
            int checkedHere=0;
            foreach(var group in groups.Where(g=>g.Count>1))
            {
                var polygons=group.Select(p=>p.Item2).ToList();var all=polygons.SelectMany(p=>p).ToList();double minX=all.Min(p=>p.X)+16.1,maxX=all.Max(p=>p.X)-16.1,minY=all.Min(p=>p.Y)+16.1,maxY=all.Max(p=>p.Y)-16.1;
                if(minX>maxX||minY>maxY)continue;var candidates=new List<Vec3>{new Vec3((minX+maxX)/2,(minY+maxY)/2,0)};
                for(double x=minX;x<=maxX;x+=Math.Max(32,(maxX-minX)/24))for(double y=minY;y<=maxY;y+=Math.Max(32,(maxY-minY)/24))candidates.Add(new Vec3(x,y,0));
                foreach(var candidate in candidates)
                {
                    if(checkedHere++>=limit)return null;attempts++;var player=new Vec3(candidate.X,candidate.Y,group[0].Item1+37);
                    if(obstacles.Any(b=>ConvexGeometryValidator.IntersectsPlayer(b,player)))continue;
                    var cells=new List<List<Vec3>>{new List<Vec3>{candidate+new Vec3(-16,-16,0),candidate+new Vec3(16,-16,0),candidate+new Vec3(16,16,0),candidate+new Vec3(-16,16,0)}};bool excessive=false;
                    foreach(var polygon in polygons)
                    {
                        for(int i=0;i<polygon.Count;i++)
                        {
                            var a=polygon[i];var d=polygon[(i+1)%polygon.Count]-a;var n=new Vec3(-d.Y,d.X,0).Unit();double distance=n.Dot(a);var next=new List<List<Vec3>>();
                            foreach(var cell in cells){if(cell.Min(p=>n.Dot(p)-distance)<-t&&cell.Max(p=>n.Dot(p)-distance)>t){next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell,n,distance),t));next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell,-n,-distance),t));}else next.Add(cell);}
                            cells=next.Where(c=>c.Count>=3&&Math.Abs(PolygonGeometry.Area(c))>t*t).ToList();if(cells.Count>10000){excessive=true;break;}
                        }
                        if(excessive)break;
                    }
                    if(excessive)continue;
                    bool support=cells.Count>0&&cells.All(cell=>{var center=cell.Aggregate(Vec3.Zero,(a,b)=>a+b)/cell.Count;return polygons.Any(p=>PolygonGeometry.Inside(p,center));});
                    if(support)return player;
                }
            }
            return null;
        }
        public static void AddBasicEntities(MapDocument map, Vec3 spawn)
        {
            var player = new MapEntity("info_player_start"); player.Properties.Add("origin", spawn.ToString()); player.Properties.Add("angle", "0"); map.Entities.Add(player);
            var light = new MapEntity("light"); light.Properties.Add("origin", (spawn + new Vec3(0, 0, 12)).ToString()); light.Properties.Add("_light", "255 255 255 300"); map.Entities.Add(light);
        }
    }
}
