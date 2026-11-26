import { unifiedDataChannel, uniformDataStruct, uniformTypeDescriptor } from '@kit.ArkData';
import { hilog } from '@kit.PerformanceAnalysisKit';

interface DragRequest {
  gesture: number;
  text?: string;
  texts?: string[];
  items?: DragItem[];
  effects?: number;
}

interface DragItem {
  text?: string;
  html?: string;
  link?: string;
  filePath?: string;
}

interface DragGesture {
  pointerId?: number;
  source: AvaloniaTextDragSource;
}

interface DragSession {
  source: AvaloniaTextDragSource;
  finish: (effect: string | null) => void;
}

interface MouseDragOffer {
  session: DragSession;
  data: unifiedDataChannel.UnifiedData;
  label: string;
}

interface WaitingMouseDrag {
  source: AvaloniaTextDragSource;
  token: number;
  event: DragEvent;
  timeout: number;
}

// Negative gesture tokens never collide with ArkUI's touch IDs.
let nextContact = -1;
const eligibleGestures: Map<number, DragGesture> = new Map();
let activeDrag: DragSession | undefined;
let waitingMouseDrag: WaitingMouseDrag | undefined;
const mouseOfferWaitMs = 80;

export class AvaloniaTextDragSource {
  private contacts: Map<number, number> = new Map();
  private mouseToken: number | undefined;
  private mouseOffer: MouseDragOffer | undefined;
  private mouseDrag: DragSession | undefined;
  private startDrag: (pointerId: number, data: unifiedDataChannel.UnifiedData, label: string) => Promise<string | null>;
  private notifyMouseStart: (waiting: boolean, updateEvent?: () => void) => void;

  constructor(startDrag: (pointerId: number, data: unifiedDataChannel.UnifiedData, label: string) => Promise<string | null>,
    notifyMouseStart: (waiting: boolean, updateEvent?: () => void) => void) {
    this.startDrag = startDrag;
    this.notifyMouseStart = notifyMouseStart;
  }

  contact(pointerId: number, phase: number): number {
    const existing = this.contacts.get(pointerId);
    if (phase === 0 && Number.isInteger(pointerId) && pointerId >= 0 && pointerId <= 9) {
      if (existing !== undefined) {
        eligibleGestures.delete(existing);
      }
      const token = nextContact--;
      this.contacts.set(pointerId, token);
      eligibleGestures.set(token, { pointerId, source: this });
      return token;
    }
    if (existing !== undefined && (phase === 2 || phase === 3)) {
      this.contacts.delete(pointerId);
      eligibleGestures.delete(existing);
    }
    return existing ?? pointerId;
  }

  revokeGestures(): void {
    for (const token of this.contacts.values()) {
      eligibleGestures.delete(token);
    }
    this.endMousePress();
  }

  surfaceDestroyed(): void {
    this.revokeGestures();
    this.contacts.clear();
    if (this.mouseDrag !== undefined) {
      this.finishMouseDrag(null);
    } else if (activeDrag?.source === this) {
      // Detach the managed waiter. The system will end its own drag and may still call its Promise.
      activeDrag.finish(null);
    }
  }

  beginMousePress(): number {
    this.endMousePress();
    const token = nextContact--;
    this.mouseToken = token;
    eligibleGestures.set(token, { source: this });
    return token;
  }

  endMousePress(): void {
    if (this.mouseToken !== undefined) {
      eligibleGestures.delete(this.mouseToken);
      this.mouseToken = undefined;
    }
    this.mouseOffer?.session.finish(null);
    this.mouseOffer = undefined;
    const waiting = this.takeWaitingMouseDrag();
    if (waiting !== undefined) {
      this.cancelWaitingMouseDrag(waiting);
    }
  }

  startMouseDrag(event: DragEvent, waitingPreview?: DragItemInfo): DragItemInfo | undefined {
    const offer = this.mouseOffer;
    this.mouseOffer = undefined;
    if (offer === undefined) {
      const token = this.mouseToken;
      if (token !== undefined && eligibleGestures.has(token) && activeDrag === undefined &&
        waitingMouseDrag === undefined && waitingPreview !== undefined) {
        // ArkUI's mouse recognizer can reach drag start before the managed press handler posts its offer.
        waitingMouseDrag = { source: this, token, event,
          timeout: setTimeout((): void => this.endMousePress(), mouseOfferWaitMs) };
        try {
          this.notifyMouseStart(true);
          hilog.debug(0x0202, 'AvaloniaOHOS', 'System mouse drag waiting for a payload offer');
          return waitingPreview;
        } catch (_error) {
          this.takeWaitingMouseDrag();
          hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not defer system mouse drag');
        }
      }
      this.endMousePress();
      return undefined;
    }
    if (activeDrag !== undefined) {
      offer.session.finish(null);
      return undefined;
    }

    // ArkUI's proactive executeDrag accepts touch IDs only. The component owns the native mouse gesture.
    let stage = 'preview';
    try {
      const preview = AvaloniaTextDragSource.createPreview(offer.label);
      stage = 'set data';
      event.setData(offer.data);
      this.mouseDrag = offer.session;
      activeDrag = offer.session;
      hilog.debug(0x0202, 'AvaloniaOHOS', 'System mouse drag prepared a payload preview');
      return preview;
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'System mouse drag failed at %{public}s (%{public}s)',
        stage, (error as Error).name);
      offer.session.finish(null);
      return undefined;
    }
  }

  finishMouseDrag(effect: string | null): void {
    if (this.takeWaitingMouseDrag() !== undefined) {
      this.endMousePress();
    }
    const drag = this.mouseDrag;
    this.mouseDrag = undefined;
    if (drag !== undefined) {
      if (activeDrag === drag) {
        activeDrag = undefined;
      }
      drag.finish(effect);
    }
  }

  private takeWaitingMouseDrag(): WaitingMouseDrag | undefined {
    const waiting = waitingMouseDrag;
    if (waiting?.source !== this) {
      return undefined;
    }
    waitingMouseDrag = undefined;
    clearTimeout(waiting.timeout);
    return waiting;
  }

  private cancelWaitingMouseDrag(waiting: WaitingMouseDrag): void {
    try {
      this.notifyMouseStart(false, (): void => waiting.event.setResult(DragResult.DRAG_CANCELED));
      hilog.debug(0x0202, 'AvaloniaOHOS', 'System mouse drag ended before a payload offer arrived');
    } catch (_error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not cancel deferred system mouse drag');
    }
  }

  cancelContacts(): number[] {
    const canceled: number[] = [];
    for (const token of this.contacts.values()) {
      eligibleGestures.delete(token);
      canceled.push(token);
    }
    this.contacts.clear();
    return canceled;
  }

  static result(event: DragEvent): string | null {
    if (event.getResult() !== DragResult.DRAG_SUCCESSFUL) {
      return null;
    }
    return event.dragBehavior === DragBehavior.MOVE ? 'move' : 'copy';
  }

  // Draw only the offered payload. Capturing the XComponent would expose the whole Avalonia surface.
  static createPreview(label: string): DragItemInfo {
    let stage = 'canvas';
    try {
      return AvaloniaTextDragSource.drawPreview(label, (value: string): void => { stage = value; });
    } catch (error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'System drag preview failed at %{public}s (%{public}s)',
        stage, (error as Error).name);
      throw new Error('Could not render a system drag preview');
    }
  }

  private static drawPreview(label: string, setStage: (value: string) => void): DragItemInfo {
    const maxWidth = 240;
    const height = 42;
    const padding = 14;
    const context = new OffscreenCanvasRenderingContext2D(maxWidth, height, new RenderingContextSettings(true));
    setStage('measure text');
    context.font = '500 14vp sans-serif';
    let text = label;
    const maxTextWidth = maxWidth - 2 * padding;
    if (context.measureText(text).width > maxTextWidth) {
      const characters = Array.from(text);
      do {
        characters.pop();
        text = characters.join('') + '…';
      } while (characters.length > 0 && context.measureText(text).width > maxTextWidth);
    }
    const width = Math.min(maxWidth, Math.max(64, Math.ceil(context.measureText(text).width) + 2 * padding));
    setStage('draw card');
    context.beginPath();
    context.roundRect(0, 0, width, height, 8);
    context.fillStyle = '#24549B';
    context.fill();
    context.fillStyle = '#FFFFFF';
    context.textBaseline = 'middle';
    context.fillText(text, padding, height / 2, maxTextWidth);
    setStage('read pixels');
    const pixelMap = context.getPixelMap(0, 0, width, height);
    if (!pixelMap) {
      throw new Error('Could not render a system drag preview');
    }
    return { pixelMap };
  }

  private static createTextData(texts: string[]): unifiedDataChannel.UnifiedData | undefined {
    if (!Array.isArray(texts) || texts.length === 0 || texts.length > 32) {
      return undefined;
    }
    const data = new unifiedDataChannel.UnifiedData();
    let units = 0;
    for (const text of texts) {
      if (typeof text !== 'string') {
        return undefined;
      }
      units += text.length;
      if (units > 64 * 1024) {
        return undefined;
      }
      const record = new unifiedDataChannel.PlainText();
      record.textContent = text;
      data.addRecord(record);
    }
    return data;
  }

  private static createRichData(items: DragItem[]): unifiedDataChannel.UnifiedData | undefined {
    const data = new unifiedDataChannel.UnifiedData();
    let units = 0;
    for (const item of items) {
      // ArkUI can grant write access for a dragged file URI even when its record requests read access only.
      if (item === null || typeof item !== 'object' || item.filePath !== undefined) {
        return undefined;
      }
      const record = new unifiedDataChannel.UnifiedRecord();
      if (typeof item.text === 'string') {
        units += item.text.length;
        const text: uniformDataStruct.PlainText = { uniformDataType: 'general.plain-text', textContent: item.text };
        record.addEntry(uniformTypeDescriptor.UniformDataType.PLAIN_TEXT, text);
      }
      if (typeof item.html === 'string') {
        units += item.html.length;
        const html: uniformDataStruct.HTML = {
          uniformDataType: 'general.html', htmlContent: item.html, plainContent: item.text,
          uriAuthorizationPolicies: [unifiedDataChannel.UriPermission.NONE]
        };
        record.addEntry(uniformTypeDescriptor.UniformDataType.HTML, html);
      }
      if (typeof item.link === 'string' && /^https?:\/\//i.test(item.link)) {
        units += item.link.length;
        const link: uniformDataStruct.Hyperlink = { uniformDataType: 'general.hyperlink', url: item.link };
        record.addEntry(uniformTypeDescriptor.UniformDataType.HYPERLINK, link);
      }
      if (units > 64 * 1024 || record.getTypes().length === 0) {
        return undefined;
      }
      data.addRecord(record);
    }
    return data;
  }

  private static previewLabel(text: string | undefined): string {
    if (text === undefined) {
      return '';
    }
    const label = text.slice(0, 160).replace(/[\uD800-\uDBFF]$/, '').replace(/\s+/g, ' ').trim();
    return label && text.length > 160 ? label + '…' : label;
  }

  static async execute(operation: string, argument: string): Promise<string | null> {
    const request = JSON.parse(argument) as DragRequest;
    const gesture = eligibleGestures.get(request.gesture);
    if (gesture === undefined) {
      return null;
    }
    eligibleGestures.delete(request.gesture);
    if (activeDrag !== undefined || (waitingMouseDrag !== undefined && waitingMouseDrag.token !== request.gesture)) {
      return null;
    }
    let data: unifiedDataChannel.UnifiedData | undefined;
    let label = 'Text';
    let effects = 1;
    if (operation === 'drag.data') {
      effects = request.effects ?? 0;
      const items = request.items;
      if (!Array.isArray(items) || items.length === 0 || items.length > 32 ||
        (effects !== 1 && effects !== 2 && effects !== 3)) {
        return null;
      }
      data = AvaloniaTextDragSource.createRichData(items);
      if (data !== undefined) {
        const first = items[0];
        label = AvaloniaTextDragSource.previewLabel(typeof first.text === 'string' ? first.text : undefined);
        if (!label) {
          label = typeof first.link === 'string' && /^https?:\/\//i.test(first.link)
            ? AvaloniaTextDragSource.previewLabel(first.link)
            : typeof first.html === 'string' ? 'Formatted text' : 'Text';
        }
      }
    } else {
      const texts = operation === 'drag.texts' ? request.texts :
        typeof request.text === 'string' ? [request.text] : undefined;
      if (texts !== undefined) {
        data = AvaloniaTextDragSource.createTextData(texts);
        if (data !== undefined) {
          label = AvaloniaTextDragSource.previewLabel(texts[0]) || 'Text';
        }
      }
    }
    if (data === undefined) {
      return null;
    }
    return gesture.source.startSession(gesture.pointerId, data, label, effects);
  }

  private startSession(pointerId: number | undefined, data: unifiedDataChannel.UnifiedData,
    label: string, effects: number): Promise<string | null> {
    return new Promise<string | null>((resolve: (value: string | null) => void): void => {
      const session: DragSession = {
        source: this,
        finish: (effect: string | null): void => {
          const bit = effect === 'copy' ? 1 : effect === 'move' ? 2 : 0;
          resolve((effects & bit) !== 0 ? effect : null);
        }
      };
      if (pointerId === undefined) {
        const waiting = this.takeWaitingMouseDrag();
        if (waiting === undefined) {
          this.mouseOffer = { session, data, label };
          return;
        }
        try {
          this.notifyMouseStart(false, (): void => {
            waiting.event.setData(data);
            this.mouseDrag = session;
            activeDrag = session;
          });
          hilog.debug(0x0202, 'AvaloniaOHOS', 'System mouse drag resumed with a payload offer');
        } catch (_error) {
          this.finishMouseDrag(null);
          session.finish(null);
          this.cancelWaitingMouseDrag(waiting);
        }
        return;
      }
      activeDrag = session;
      const settle = (effect: string | null): void => {
        if (activeDrag === session) {
          activeDrag = undefined;
        }
        session.finish(effect);
      };
      try {
        this.startDrag(pointerId, data, label).then(settle, (): void => settle(null));
      } catch (_error) {
        settle(null);
      }
    });
  }
}
