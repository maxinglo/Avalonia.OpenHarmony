import { unifiedDataChannel, uniformDataStruct, uniformTypeDescriptor } from '@kit.ArkData';
import { fileIo } from '@kit.CoreFileKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import bridge from 'libavalonia_ohos_bridge.so';

const textType = uniformTypeDescriptor.UniformDataType.PLAIN_TEXT;
const htmlType = uniformTypeDescriptor.UniformDataType.HTML;
const hyperlinkType = uniformTypeDescriptor.UniformDataType.HYPERLINK;
const fileUriType = uniformTypeDescriptor.UniformDataType.FILE_URI;
const fileTypes: string[] = [uniformTypeDescriptor.UniformDataType.FILE, fileUriType,
  uniformTypeDescriptor.UniformDataType.IMAGE, uniformTypeDescriptor.UniformDataType.VIDEO,
  uniformTypeDescriptor.UniformDataType.AUDIO];
export const avaloniaDropTypes: string[] = [textType, htmlType, hyperlinkType, ...fileTypes];
const maxItems = 32;
const maxPayloadLength = 1_048_576;
const allowTextMove = 16;

interface FileOffer {
  uri: string;
  wantsWrite: boolean;
}

interface DroppedFile {
  uri: string;
  descriptor: number;
  length: number;
  writable: boolean;
}

interface DropItem {
  text?: string;
  html?: string;
  link?: string;
  file?: DroppedFile;
}

interface DropPayload {
  items: DropItem[];
}

interface LegacyDropPayload {
  texts: string[];
  files: DroppedFile[];
}

export class AvaloniaDropReceiver {
  dispatch(surfaceId: string, phase: number, x: number, y: number, event: DragEvent): void {
    const modifiers = (event.getModifierKeyState?.(['Alt']) ? 1 : 0)
      | (event.getModifierKeyState?.(['Ctrl']) ? 2 : 0)
      | (event.getModifierKeyState?.(['Shift']) ? 4 : 0);
    // Move requires an explicit Shift gesture and a destination that selects Move for its non-file data.
    let formats = (modifiers & 4) !== 0 ? allowTextMove : 0;
    if (phase === 3) {
      this.drop(surfaceId, x, y, modifiers, formats, event);
      return;
    }
    const summary = event.getSummary().summary;
    if (summary[textType] !== undefined || summary[htmlType] !== undefined || summary[hyperlinkType] !== undefined) {
      formats |= 1;
    }
    if (fileTypes.some((type: string): boolean => summary[type] !== undefined)) {
      formats |= 2;
    }
    if (summary[htmlType] !== undefined) {
      formats |= 4;
    }
    if (summary[hyperlinkType] !== undefined) {
      formats |= 8;
    }
    const effect = bridge.dragEffect(surfaceId, phase, x, y, modifiers, formats, '');
    event.dragBehavior = effect === 2 ? DragBehavior.MOVE : DragBehavior.COPY;
    event.setResult(effect === 0 ? DragResult.DROP_DISABLED : DragResult.DROP_ENABLED);
  }

  private drop(surfaceId: string, x: number, y: number, modifiers: number, formats: number, event: DragEvent): void {
    const opened: fileIo.File[] = [];
    let effect = 0;
    try {
      const records = event.getData().getRecords();
      const payload: DropPayload = { items: [] };
      let textLength = 0;
      let hasFileOffers = false;
      if (records.length <= maxItems) {
        for (const record of records) {
          const types = record.getTypes();
          hasFileOffers = hasFileOffers || fileTypes.some((type: string): boolean => types.includes(type));
          const item = this.readTextFormats(record, types);
          const fileOffer = this.readFileOffer(record, types);
          if (fileOffer !== undefined) {
            const file = this.openFile(fileOffer, opened);
            if (file === undefined) {
              continue;
            }
            item.file = file;
          }
          textLength += (item.text?.length ?? 0) + (item.html?.length ?? 0) + (item.link?.length ?? 0);
          if (textLength > maxPayloadLength) {
            break;
          }
          if (item.text !== undefined || item.html !== undefined || item.link !== undefined || item.file !== undefined) {
            payload.items.push(item);
          }
        }
      }
      if (textLength <= maxPayloadLength && payload.items.length > 0) {
        if (hasFileOffers) {
          formats |= 2;
        }
        effect = bridge.dragEffect(surfaceId, 3, x, y, modifiers, formats, this.encodePayload(payload));
      }
    } catch (_error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not read the system drop data');
    } finally {
      for (const file of opened) {
        try {
          fileIo.closeSync(file);
        } catch (_error) {
          hilog.warn(0x0202, 'AvaloniaOHOS', 'Could not close a dropped file descriptor');
        }
      }
    }
    if (effect === 0) {
      bridge.drag(surfaceId, 2, x, y, modifiers, 0, '');
    }
    event.dragBehavior = effect === 2 ? DragBehavior.MOVE : DragBehavior.COPY;
    event.setResult(effect === 0 ? DragResult.DRAG_FAILED : DragResult.DRAG_SUCCESSFUL);
  }

  private encodePayload(payload: DropPayload): string {
    if (payload.items.some((item: DropItem): boolean => item.html !== undefined || item.link !== undefined ||
      item.file?.writable === true || (item.file !== undefined && item.text !== undefined))) {
      return JSON.stringify(payload);
    }
    const legacy: LegacyDropPayload = { texts: [], files: [] };
    for (const item of payload.items) {
      if (item.text !== undefined) {
        legacy.texts.push(item.text);
      }
      if (item.file !== undefined) {
        legacy.files.push(item.file);
      }
    }
    return JSON.stringify(legacy);
  }

  private readTextFormats(record: unifiedDataChannel.UnifiedRecord, types: string[]): DropItem {
    const item: DropItem = {};
    if (types.includes(textType)) {
      item.text = this.readText(record);
    }
    if (types.includes(htmlType)) {
      const entry = record instanceof unifiedDataChannel.HTML ? record : record.getEntry(htmlType);
      if (typeof entry === 'object' && entry !== null) {
        const html = entry as uniformDataStruct.HTML;
        item.html = typeof html.htmlContent === 'string' ? html.htmlContent : undefined;
        if (item.text === undefined && typeof html.plainContent === 'string' && html.plainContent) {
          item.text = html.plainContent;
        }
      }
    }
    if (types.includes(hyperlinkType)) {
      const entry = record instanceof unifiedDataChannel.Hyperlink ? record : record.getEntry(hyperlinkType);
      if (typeof entry === 'object' && entry !== null) {
        const url = (entry as uniformDataStruct.Hyperlink).url;
        if (typeof url === 'string' && /^https?:\/\//i.test(url)) {
          item.link = url;
          item.text = item.text ?? url;
        }
      }
    }
    return item;
  }

  private readText(record: unifiedDataChannel.UnifiedRecord): string | undefined {
    if (record instanceof unifiedDataChannel.PlainText) {
      return record.textContent;
    }
    const entry = record.getEntry(textType);
    if (typeof entry === 'string') {
      return entry;
    }
    if (typeof entry === 'object' && entry !== null) {
      const value = (entry as uniformDataStruct.PlainText).textContent;
      return typeof value === 'string' ? value : undefined;
    }
    return undefined;
  }

  private readFileOffer(record: unifiedDataChannel.UnifiedRecord, types: string[]): FileOffer | undefined {
    if (types.includes(fileUriType)) {
      const entry = record.getEntry(fileUriType);
      if (typeof entry === 'object' && entry !== null) {
        const file = entry as uniformDataStruct.FileUri;
        if (typeof file.oriUri === 'string' && file.fileType !== uniformTypeDescriptor.UniformDataType.FOLDER) {
          // Keep legacy and unspecified policies read-only. The read/write open must also pass FileKit authorization.
          const wantsWrite = file.uriAuthorizationPolicies?.includes(unifiedDataChannel.UriPermission.WRITE) === true;
          return { uri: file.oriUri, wantsWrite };
        }
      }
    }
    if (record instanceof unifiedDataChannel.File && fileTypes.some((type: string): boolean => types.includes(type))) {
      return { uri: record.uri, wantsWrite: false };
    }
    return undefined;
  }

  private openFile(offer: FileOffer, opened: fileIo.File[]): DroppedFile | undefined {
    const uri = offer.uri;
    if (!uri.toLowerCase().startsWith('file://') || uri.length > 4096 || uri.includes('\u0000') ||
      uri.endsWith('/') || uri.includes('?') || uri.includes('#')) {
      return undefined;
    }
    let file: fileIo.File | undefined;
    let writable = false;
    if (offer.wantsWrite) {
      try {
        file = fileIo.openSync(uri, fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.NONBLOCK);
        writable = true;
      } catch (_error) {
      }
    }
    try {
      // FileKit checks the temporary grant while the native drop callback is still running.
      file = file ?? fileIo.openSync(uri, fileIo.OpenMode.READ_ONLY | fileIo.OpenMode.NONBLOCK);
      opened.push(file);
      const stat = fileIo.statSync(file.fd);
      if (stat.isFile()) {
        return { uri, descriptor: file.fd, length: stat.size, writable };
      }
    } catch (_error) {
      hilog.warn(0x0202, 'AvaloniaOHOS', 'A dropped file could not be opened for reading');
    }
    return undefined;
  }
}
