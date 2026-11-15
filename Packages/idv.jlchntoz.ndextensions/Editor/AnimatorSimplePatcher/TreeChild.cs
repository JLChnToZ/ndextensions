
namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    sealed class TreeChild {
        public readonly string parameter;
        public readonly float x, y;
        public readonly TreeStatement tree;
        public readonly ImportStatement import;
        public readonly AnimationStatement[] animations;

        public TreeChild(string parameter, float x, float y, TreeStatement tree, ImportStatement import, AnimationStatement[] animations) {
            this.parameter = parameter;
            this.x = x;
            this.y = y;
            this.tree = tree;
            this.import = import;
            this.animations = animations;
        }
    }
}