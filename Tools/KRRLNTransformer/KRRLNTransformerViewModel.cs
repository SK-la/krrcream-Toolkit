using krrTools.Bindable;
using krrTools.Configuration;
using krrTools.Core;
using Microsoft.Extensions.DependencyInjection;

namespace krrTools.Tools.KRRLNTransformer
{
    public class KRRLNTransformerViewModel : ToolViewModelBase<KRRLNTransformerOptions>, IPreviewOptionsProvider
    {
        // private readonly IEventBus _eventBus;
        
        public KRRLNTransformerViewModel(KRRLNTransformerOptions options) : base(ConverterEnum.KRRLN, true, options)
        {
            App.Services.GetRequiredService<IEventBus>();
            
            // Subscribe to all Bindable<T> property changes
            SubscribeToPropertyChanges();
        }

        private void SubscribeToPropertyChanges()
        {
            // Bindable<T> properties automatically handle change notifications
            // No manual subscription needed for UI updates
        }

        public IToolOptions GetPreviewOptions()
        {
            var previewOptions = new KRRLNTransformerOptions();
            previewOptions.ShortPercentage.Value = Options.ShortPercentage.Value;
            previewOptions.ShortLevel.Value = Options.ShortLevel.Value;
            previewOptions.ShortLimit.Value = Options.ShortLimit.Value;
            previewOptions.ShortRandom.Value = Options.ShortRandom.Value;

            previewOptions.LongPercentage.Value = Options.LongPercentage.Value;
            previewOptions.LongLevel.Value = Options.LongLevel.Value;
            previewOptions.LongLimit.Value = Options.LongLimit.Value;
            previewOptions.LongRandom.Value = Options.LongRandom.Value;

            previewOptions.LengthThreshold.Value = Options.LengthThreshold.Value;
            previewOptions.Alignment.Value = Options.Alignment.Value;
            previewOptions.LNAlignment.Value = Options.LNAlignment.Value;

            previewOptions.ProcessOriginalIsChecked.Value = Options.ProcessOriginalIsChecked.Value;
            previewOptions.ODValue.Value = Options.ODValue.Value;

            previewOptions.Seed.Value = Options.Seed.Value;

            return previewOptions;
        }
    }
}
