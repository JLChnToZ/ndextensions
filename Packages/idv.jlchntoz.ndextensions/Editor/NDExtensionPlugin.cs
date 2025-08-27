using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using JLChnToZ.NDExtensions.Editors;

[assembly: ExportsPlugin(typeof(NDExtensionPlugin))]

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class NDExtensionPlugin : Plugin<NDExtensionPlugin> {
        public override string QualifiedName => "idv.jlchntoz.ndextension";

        public override string DisplayName => "Vistanz's Non Destructive Plugin";

        protected override void Configure() {
            InPhase(BuildPhase.Resolving)
            .Run(ParameterResolverPass.Instance);
            InPhase(BuildPhase.Resolving)
            .WithRequiredExtension(
                typeof(RebakeHumanoidContext),
                seq => seq.Run(GetBareFootPass.Instance)
            );
            InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .WithRequiredExtension(
                typeof(AnimatorServicesContext),
                seq => {
                    seq.OnPlatforms(
                        new[] { WellKnownPlatforms.VRChatAvatar30 },
                        seq2 => seq2.Run(FeatureMaintainerPass.Instance)
                    );
                    seq.Run(ParameterValueFilterPass.Instance);
                    seq.Run(ConstraintReducerPass.Instance);
                    seq.WithRequiredExtension(
                        typeof(RebakeHumanoidContext),
                        seq2 => seq2.Run(RebakeHumanoidPass.Instance)
                    );
                    seq.OnPlatforms(
                        new[] { WellKnownPlatforms.VRChatAvatar30 },
                        seq2 => seq2.WithRequiredExtension(
                            typeof(ParameterCompressorContext),
                            seq3 => seq3.Run(ParameterCompressorPrePass.Instance)
                        )
                    );
                }
            );
            InPhase(BuildPhase.Optimizing)
            .BeforePlugin("com.anatawa12.avatar-optimizer")
            .OnPlatforms(
                new[] { WellKnownPlatforms.VRChatAvatar30 },
                seq => seq.WithRequiredExtension(
                    typeof(ParameterCompressorContext),
                    seq2 => seq2.Run(ParameterCompressorPass.Instance)
                )
            );
        }
    }
}