using System.Windows.Media;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Notch;

enum DeviceKind { Charger, Usb, Dongle, Phone, Generic, Quest3 }

// hand-built low-poly 3D models for the "device connected" card. everything is
// made out of simple boxes so it renders fast and reads clearly while spinning.
// the plug / connector part is always light gray, the body color is passed in.
static class DeviceModels
{
    static readonly Color Plug = Color.FromRgb(0xCF, 0xD4, 0xDA);   // light gray connectors
    static readonly Color Dark = Color.FromRgb(0x20, 0x22, 0x26);

    public static Model3D Build(DeviceKind kind, Color accent, Color body, double len)
    {
        var g = new Model3DGroup();
        switch (kind)
        {
            case DeviceKind.Charger:   // white brick + two light-gray prongs
                g.Children.Add(Box(0, 0, 0, 1.1, 1.3, 0.7, Colors.White));
                g.Children.Add(Box(-0.24, 0.95, 0, 0.12, 0.5, 0.12, Plug));
                g.Children.Add(Box(0.24, 0.95, 0, 0.12, 0.5, 0.12, Plug));
                break;

            case DeviceKind.Dongle:    // a wireless receiver: colored body + light-gray plug
                g.Children.Add(Box(0, -0.1, 0, 0.5, len, 0.26, body));
                g.Children.Add(Box(0, len / 2 + 0.12, 0, 0.34, 0.42, 0.13, Plug));
                break;

            case DeviceKind.Usb:       // long thin gray stick + light-gray usb-a plug
                g.Children.Add(Box(0, -0.15, 0, 0.42, len, 0.2, body));
                g.Children.Add(Box(0, len / 2 + 0.18, 0, 0.5, 0.46, 0.15, Plug));
                g.Children.Add(Box(0, len / 2 + 0.18, 0.08, 0.34, 0.28, 0.03, Dark));   // the slot
                break;

            case DeviceKind.Quest3:    // white stadium visor (rounded ends), 3 center camera pills
                var white = Color.FromRgb(0xF3, 0xF3, 0xF5);
                var vblk = Color.FromRgb(0x1A, 0x1A, 0x1E);
                var vlens = Color.FromRgb(0x38, 0x36, 0x42);
                double vr = 0.34;                                            // half-height / end radius
                g.Children.Add(Box(0, 0, 0, 0.86, vr * 2, 0.42, white));     // flat middle (narrower)
                g.Children.Add(CylZ(-0.43, 0, 0, 0.42, vr, white));          // rounded left end
                g.Children.Add(CylZ(0.43, 0, 0, 0.42, vr, white));           // rounded right end
                g.Children.Add(Box(-0.4, -0.04, 0.21, 0.12, 0.36, 0.04, vblk));   // three spread-out pills
                g.Children.Add(Box(0.0, -0.04, 0.21, 0.12, 0.36, 0.04, vlens));
                g.Children.Add(Box(0.4, -0.04, 0.21, 0.12, 0.36, 0.04, vblk));
                g.Children.Add(Box(0, -0.06, -0.3, 0.8, 0.5, 0.28, vblk));   // dark facial interface (back)
                g.Children.Add(Box(0, 0.36, -0.18, 0.32, 0.09, 0.6, white)); // strap over the top, back
                break;

            case DeviceKind.Phone:     // a thin slab with a lighter screen face
                g.Children.Add(Box(0, 0, 0, 0.8, 1.5, 0.12, Dark));
                g.Children.Add(Box(0, 0, 0.07, 0.66, 1.32, 0.02, accent));
                break;

            default:                   // a plain accent cube
                g.Children.Add(Box(0, 0, 0, 1, 1, 1, accent));
                break;
        }
        return g;
    }

    // an elliptical cylinder lying along the X axis, for rounded shapes
    static GeometryModel3D Cyl(double cx, double cy, double cz, double len, double ry, double rz, Color color, int seg = 26)
    {
        var m = new MeshGeometry3D();
        double hl = len / 2;
        for (int i = 0; i < seg; i++)
        {
            double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
            double y0 = Math.Cos(a0) * ry, z0 = Math.Sin(a0) * rz;
            double y1 = Math.Cos(a1) * ry, z1 = Math.Sin(a1) * rz;
            int b = m.Positions.Count;
            m.Positions.Add(new Point3D(cx - hl, cy + y0, cz + z0));
            m.Positions.Add(new Point3D(cx + hl, cy + y0, cz + z0));
            m.Positions.Add(new Point3D(cx + hl, cy + y1, cz + z1));
            m.Positions.Add(new Point3D(cx - hl, cy + y1, cz + z1));
            var n0 = new Vector3D(0, y0 / ry, z0 / rz); n0.Normalize();
            var n1 = new Vector3D(0, y1 / ry, z1 / rz); n1.Normalize();
            m.Normals.Add(n0); m.Normals.Add(n0); m.Normals.Add(n1); m.Normals.Add(n1);
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 2);
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 3);
        }
        Cap(m, cx - hl, cy, cz, ry, rz, seg, -1);
        Cap(m, cx + hl, cy, cz, ry, rz, seg, 1);
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 24));
        return new GeometryModel3D(m, mat) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(color)) };
    }

    // a cylinder along the Z axis (round cross-section in the X/Y plane), for rounding
    // the left/right ends of a visor into a stadium shape
    static GeometryModel3D CylZ(double cx, double cy, double cz, double len, double r, Color color, int seg = 26)
    {
        var m = new MeshGeometry3D();
        double hl = len / 2;
        for (int i = 0; i < seg; i++)
        {
            double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
            double x0 = Math.Cos(a0) * r, y0 = Math.Sin(a0) * r;
            double x1 = Math.Cos(a1) * r, y1 = Math.Sin(a1) * r;
            int b = m.Positions.Count;
            m.Positions.Add(new Point3D(cx + x0, cy + y0, cz - hl));
            m.Positions.Add(new Point3D(cx + x0, cy + y0, cz + hl));
            m.Positions.Add(new Point3D(cx + x1, cy + y1, cz + hl));
            m.Positions.Add(new Point3D(cx + x1, cy + y1, cz - hl));
            var n0 = new Vector3D(x0, y0, 0); n0.Normalize();
            var n1 = new Vector3D(x1, y1, 0); n1.Normalize();
            m.Normals.Add(n0); m.Normals.Add(n0); m.Normals.Add(n1); m.Normals.Add(n1);
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 2);
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 3);
        }
        CapZ(m, cz + hl, cx, cy, r, seg, 1);
        CapZ(m, cz - hl, cx, cy, r, seg, -1);
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 24));
        return new GeometryModel3D(m, mat) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(color)) };
    }

    static void CapZ(MeshGeometry3D m, double z, double cx, double cy, double r, int seg, int dir)
    {
        int c = m.Positions.Count;
        m.Positions.Add(new Point3D(cx, cy, z));
        m.Normals.Add(new Vector3D(0, 0, dir));
        for (int i = 0; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg;
            m.Positions.Add(new Point3D(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, z));
            m.Normals.Add(new Vector3D(0, 0, dir));
        }
        for (int i = 1; i <= seg; i++)
        {
            m.TriangleIndices.Add(c);
            if (dir > 0) { m.TriangleIndices.Add(c + i); m.TriangleIndices.Add(c + i + 1); }
            else { m.TriangleIndices.Add(c + i + 1); m.TriangleIndices.Add(c + i); }
        }
    }

    static void Cap(MeshGeometry3D m, double x, double cy, double cz, double ry, double rz, int seg, int dir)
    {
        int c = m.Positions.Count;
        m.Positions.Add(new Point3D(x, cy, cz));
        m.Normals.Add(new Vector3D(dir, 0, 0));
        for (int i = 0; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg;
            m.Positions.Add(new Point3D(x, cy + Math.Cos(a) * ry, cz + Math.Sin(a) * rz));
            m.Normals.Add(new Vector3D(dir, 0, 0));
        }
        for (int i = 1; i <= seg; i++)
        {
            m.TriangleIndices.Add(c);
            if (dir > 0) { m.TriangleIndices.Add(c + i); m.TriangleIndices.Add(c + i + 1); }
            else { m.TriangleIndices.Add(c + i + 1); m.TriangleIndices.Add(c + i); }
        }
    }

    static GeometryModel3D Box(double cx, double cy, double cz, double w, double h, double d, Color color)
    {
        var mesh = BoxMesh(cx, cy, cz, w, h, d);
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 24));
        return new GeometryModel3D(mesh, mat) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(color)) };
    }

    static MeshGeometry3D BoxMesh(double cx, double cy, double cz, double w, double h, double d)
    {
        double x = w / 2, y = h / 2, z = d / 2;
        var m = new MeshGeometry3D();

        void Quad(Point3D a, Point3D b, Point3D c, Point3D e, Vector3D n)
        {
            int i = m.Positions.Count;
            m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c); m.Positions.Add(e);
            for (int k = 0; k < 4; k++) m.Normals.Add(n);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 2); m.TriangleIndices.Add(i + 3);
        }

        Point3D P(double px, double py, double pz) => new(cx + px, cy + py, cz + pz);
        var flt = P(-x, -y, z); var frt = P(x, -y, z); var trt = P(x, y, z); var tlt = P(-x, y, z);
        var flb = P(-x, -y, -z); var frb = P(x, -y, -z); var trb = P(x, y, -z); var tlb = P(-x, y, -z);

        Quad(flt, frt, trt, tlt, new Vector3D(0, 0, 1));    // front
        Quad(frb, flb, tlb, trb, new Vector3D(0, 0, -1));   // back
        Quad(frt, frb, trb, trt, new Vector3D(1, 0, 0));    // right
        Quad(flb, flt, tlt, tlb, new Vector3D(-1, 0, 0));   // left
        Quad(tlt, trt, trb, tlb, new Vector3D(0, 1, 0));    // top
        Quad(flb, frb, frt, flt, new Vector3D(0, -1, 0));   // bottom
        return m;
    }
}
