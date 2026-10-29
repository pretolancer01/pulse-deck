using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PulseDeck;
static class SceneGeometry
{
    public static Material Material(Color color, bool glow = false)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(185, 220, 239)), 48));
        if (glow) group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B))));
        group.Freeze(); return group;
    }
    public static GeometryModel3D Box(double x, double y, double z, double w, double h, double d, Material material)
    {
        var mesh = new MeshGeometry3D();
        void Face(Point3D a, Point3D b, Point3D c, Point3D e)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(e);
            foreach (int n in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i + n);
        }
        double l = x - w/2, r = x + w/2, b = y, t = y+h, f = z+d/2, k=z-d/2;
        Face(new(l,b,f), new(r,b,f), new(r,t,f), new(l,t,f));
        Face(new(r,b,k), new(l,b,k), new(l,t,k), new(r,t,k));
        Face(new(r,b,f), new(r,b,k), new(r,t,k), new(r,t,f));
        Face(new(l,b,k), new(l,b,f), new(l,t,f), new(l,t,k));
        Face(new(l,t,f), new(r,t,f), new(r,t,k), new(l,t,k));
        Face(new(l,b,k), new(r,b,k), new(r,b,f), new(l,b,f));
        mesh.Freeze(); return new(mesh, material) { BackMaterial = material };
    }
    public static GeometryModel3D Torus(double radius, double tube, Material material, int segments = 96)
    {
        var mesh = new MeshGeometry3D(); const int sides=8;
        for (int i=0;i<=segments;i++) for (int j=0;j<=sides;j++)
        {
            double a=2*Math.PI*i/segments, b=2*Math.PI*j/sides;
            mesh.Positions.Add(new((radius+tube*Math.Cos(b))*Math.Cos(a), (radius+tube*Math.Cos(b))*Math.Sin(a), tube*Math.Sin(b)));
            mesh.Normals.Add(new(Math.Cos(b)*Math.Cos(a), Math.Cos(b)*Math.Sin(a), Math.Sin(b)));
        }
        for(int i=0;i<segments;i++) for(int j=0;j<sides;j++)
        {
            int a=i*(sides+1)+j,b=a+sides+1;
            foreach(int n in new[]{a,b,a+1,a+1,b,b+1}) mesh.TriangleIndices.Add(n);
        }
        mesh.Freeze(); return new(mesh,material) { BackMaterial=material };
    }
    public static Model3DGroup Lights()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(85, 100, 120)));
        group.Children.Add(new DirectionalLight(Colors.White, new(-1,-2,-3)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(100,150,210), new(2,1,2)));
        return group;
    }
}
