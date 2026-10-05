namespace Sample.Widgets;

public class Widget
{
    public string Name { get; set; } = "";

    public void Draw(int scale)
    {
        var helper = new Renderer();
        helper.Render(this);
    }

    public class Part
    {
        public int Size;
    }
}

public class Renderer
{
    public void Render(Widget widget)
    {
    }
}

public class Repository<T>
{
    public T? Find(int id) => default;
}

public enum Color
{
    Red,
    Green,
}
