using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DblDisp
{
    class Surface
    {
        public virtual void Draw(Shape shape)
        {
            shape.Draw(this);
        }
    }

    class EtchASketch : Surface
    {
        public override void Draw(Shape shape)
        {
            shape.Draw(this);
        }
    }

    class Shape
    {
        public virtual void Draw(Surface surface)
        {
            Console.WriteLine("A shape is drawn on the surface with ink.");
        }

        public virtual void Draw(EtchASketch etchASketch)
        {
            Console.WriteLine("The knobs are moved in attempt to draw the shape.");
        }
    }


    class Polygon : Shape
    {
        public override void Draw(Surface surface)
        {
            Console.WriteLine("A polygon is drawn on the surface with ink.");
        }

        public override void Draw(EtchASketch etchASketch)
        {
            Console.WriteLine("The knobs are moved in attempt to draw the polygon.");
        }
    }

    class Quadrilateral : Polygon
    {
        public override void Draw(Surface surface)
        {
            Console.WriteLine("A quadrilateral is drawn on the surface with ink.");
        }

        public override void Draw(EtchASketch etchASketch)
        {
            Console.WriteLine("The knobs are moved in attempt to draw the quadrilateral.");
        }
    }
    
    class Program
    {
        static void test_overloading()
        {
            Console.WriteLine("test_overloading:");
            var shape = new Shape();
            shape.Draw(new Surface());
            shape.Draw(new EtchASketch());
            Console.WriteLine();
        }

        static void test_staticBinding()
        {
            Console.WriteLine("test_staticBinding:");
            var shape = new Shape();
            Surface surface = new Surface();
            Surface etchASketch = new EtchASketch();
            shape.Draw(surface);
            shape.Draw(etchASketch);
            Console.WriteLine();
        }

        static void test_thruReference()
        {
            Console.WriteLine("test_thruReference:");
            var shape = new Shape();
            Surface surface = new Surface();
            Surface etchASketch = new EtchASketch();
            surface.Draw(shape);
            etchASketch.Draw(shape);
            Console.WriteLine();
        }

        static void test_doubleDispatch()
        {
            Console.WriteLine("test_doubleDispatch:");
            Surface surface = new Surface();
            Surface etchASketch = new EtchASketch();
            var shapes = new List<Shape>
                             {
                                 new Shape(),
                                 new Polygon(),
                                 new Quadrilateral(),
                              };
            foreach (Shape shape in shapes)
            {
                surface.Draw(shape);
                etchASketch.Draw(shape);
            }
            Console.WriteLine();
        }
        static void test_dynamicDispatch()
        {
            Console.WriteLine("test_dynamicDispatch:");
            Surface surface = new Surface();
            Surface etchASketch = new EtchASketch();
            var shapes = new List<Shape>
                             {
                                 new Shape(),
                                 new Polygon(),
                                 new Quadrilateral(),
                              };
            foreach (Shape shape in shapes)
            {
                shape.Draw((dynamic)surface);
                shape.Draw(etchASketch);
                shape.Draw((dynamic)etchASketch);
            }
            Console.WriteLine();
        }

        static void Main(string[] args)
        {
            test_overloading();
            test_staticBinding();
            test_thruReference();
            test_doubleDispatch();
            test_dynamicDispatch();
            Console.ReadLine();
        }
    }
}
