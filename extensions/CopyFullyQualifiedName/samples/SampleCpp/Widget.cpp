#include "Widget.h"

namespace sample::widgets
{
    void Widget::Draw(int scale)
    {
        size = scale;
    }

    void Widget::Part::Attach()
    {
    }

    void FreeFunction()
    {
        Widget widget;
        widget.Draw(1);
    }
}

int main()
{
    sample::widgets::FreeFunction();
    return 0;
}
