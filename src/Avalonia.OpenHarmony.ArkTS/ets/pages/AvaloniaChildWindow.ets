import { AvaloniaSurfaceHost } from '../components/AvaloniaSurfaceHost';

@Entry({ useSharedStorage: true })
@Component
struct AvaloniaChildWindow {
  @LocalStorageProp('avaloniaChildWindowKey') private windowKey: number = 0;
  @LocalStorageProp('avaloniaChildAccessibility') private enableAccessibility: boolean = false;
  @LocalStorageProp('avaloniaChildInboundDrop') private enableInboundDrop: boolean = false;
  @LocalStorageProp('avaloniaChildTextDragSource') private enableTextDragSource: boolean = false;

  build() {
    Stack() {
      if (this.windowKey > 0) {
        AvaloniaSurfaceHost({
          windowKey: this.windowKey,
          enableAccessibility: this.enableAccessibility,
          enableInboundDrop: this.enableInboundDrop,
          enableTextDragSource: this.enableTextDragSource
        })
      }
    }.width('100%').height('100%')
  }
}
