import { AvaloniaSurfaceHost } from '../components/AvaloniaSurfaceHost';

@Entry({ useSharedStorage: true })
@Component
struct AvaloniaChildWindow {
  @LocalStorageProp('avaloniaChildWindowKey') private windowKey: number = 0;
  @LocalStorageProp('avaloniaChildAccessibility') private enableAccessibility: boolean = false;
  @LocalStorageProp('avaloniaChildInboundDrop') private enableInboundDrop: boolean = false;

  build() {
    Stack() {
      if (this.windowKey > 0) {
        AvaloniaSurfaceHost({
          windowKey: this.windowKey,
          enableAccessibility: this.enableAccessibility,
          enableInboundDrop: this.enableInboundDrop
        })
      }
    }.width('100%').height('100%')
  }
}
