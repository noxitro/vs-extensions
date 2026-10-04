#pragma once

namespace sample
{
    namespace widgets
    {
        class Widget
        {
        public:
            void Draw(int scale);
            int size = 0;

            class Part
            {
            public:
                void Attach();
            };
        };

        enum class Color
        {
            Red,
            Green,
        };

        template <typename T>
        class Repository
        {
        public:
            T Find(int id) { return T{}; }
        };

        void FreeFunction();
    }
}

namespace sample::nested
{
    struct Point
    {
        int x;
        int y;
    };
}
