import { common } from '@kit.AbilityKit';
import { unifiedDataChannel, uniformDataStruct, uniformTypeDescriptor } from '@kit.ArkData';
import { fileIo, fileUri } from '@kit.CoreFileKit';

const fixtureName = 'avalonia-counter-writable.txt';
const fixtureText = 'Writable drag fixture\n';

// Use as Index.ets in a separate Stage application with the 2in1 device type.
@Entry
@Component
struct HarmonyDragPeer {
  @State status: string = 'Prepare fixtures for the file tests';
  private readOnlyPath: string = '';
  private writablePath: string = '';

  private prepareFile(filesDir: string, name: string): string {
    const directory = filesDir + '/' + name;
    if (!fileIo.accessSync(directory)) {
      fileIo.mkdirSync(directory);
    }
    const path = directory + '/' + fixtureName;
    const file = fileIo.openSync(path, fileIo.OpenMode.WRITE_ONLY | fileIo.OpenMode.CREATE | fileIo.OpenMode.TRUNC);
    try {
      fileIo.writeSync(file.fd, fixtureText);
    } finally {
      fileIo.closeSync(file);
    }
    return path;
  }

  private prepareFiles(): void {
    const context = this.getUIContext().getHostContext() as common.UIAbilityContext;
    this.readOnlyPath = this.prepareFile(context.filesDir, 'drag-read');
    this.writablePath = this.prepareFile(context.filesDir, 'drag-write');
    this.status = 'Read-only and writable fixtures prepared';
  }

  private startFileDrag(event: DragEvent, writable: boolean): void {
    const path = writable ? this.writablePath : this.readOnlyPath;
    if (path === '') {
      event.setResult(DragResult.DRAG_CANCELED);
      return;
    }
    const grant = writable ? [unifiedDataChannel.UriPermission.READ, unifiedDataChannel.UriPermission.WRITE]
      : [unifiedDataChannel.UriPermission.READ];
    const entry: uniformDataStruct.FileUri = {
      uniformDataType: 'general.file-uri',
      oriUri: fileUri.getUriFromPath(path),
      fileType: uniformTypeDescriptor.UniformDataType.FILE,
      uriAuthorizationPolicies: grant
    };
    const record = new unifiedDataChannel.UnifiedRecord(uniformTypeDescriptor.UniformDataType.FILE_URI, entry);
    event.setData(new unifiedDataChannel.UnifiedData(record));
  }

  private startRichDrag(event: DragEvent): void {
    const text: uniformDataStruct.PlainText = { uniformDataType: 'general.plain-text', textContent: 'ArkUI — 中文 🎧' };
    const html: uniformDataStruct.HTML = { uniformDataType: 'general.html', htmlContent: '<b>ArkUI — 中文 🎧</b>',
      plainContent: text.textContent };
    const link: uniformDataStruct.Hyperlink = { uniformDataType: 'general.hyperlink', url: 'https://avaloniaui.net/' };
    const record = new unifiedDataChannel.UnifiedRecord();
    record.addEntry(uniformTypeDescriptor.UniformDataType.PLAIN_TEXT, text);
    record.addEntry(uniformTypeDescriptor.UniformDataType.HTML, html);
    record.addEntry(uniformTypeDescriptor.UniformDataType.HYPERLINK, link);
    event.setData(new unifiedDataChannel.UnifiedData(record));
  }

  private readFileEntry(record: unifiedDataChannel.UnifiedRecord): string {
    const entry = record.getEntry(uniformTypeDescriptor.UniformDataType.FILE_URI);
    if (typeof entry !== 'object' || entry === null) {
      return 'Missing file URI';
    }
    const uri = (entry as uniformDataStruct.FileUri).oriUri;
    const file = fileIo.openSync(uri, fileIo.OpenMode.READ_ONLY);
    try {
      const buffer = new ArrayBuffer(256);
      const bytes = fileIo.readSync(file.fd, buffer);
      let writeAllowed = false;
      try {
        const writer = fileIo.openSync(uri, fileIo.OpenMode.READ_WRITE);
        fileIo.closeSync(writer);
        writeAllowed = true;
      } catch (_error) {
      }
      return 'Read ' + bytes + ' bytes. Write permission ' + (writeAllowed ? 'granted' : 'denied');
    } finally {
      fileIo.closeSync(file);
    }
  }

  private receive(event: DragEvent): void {
    try {
      const records = event.getData().getRecords();
      const formats: string[] = [];
      const files: string[] = [];
      for (const record of records) {
        for (const type of record.getTypes()) {
          formats.push(type);
          if (type === uniformTypeDescriptor.UniformDataType.FILE_URI) {
            files.push(this.readFileEntry(record));
          }
        }
      }
      this.status = formats.join(', ') + (files.length > 0 ? '\n' + files.join('\n') : '');
      event.dragBehavior = event.getModifierKeyState?.(['Shift']) && files.length === 0
        ? DragBehavior.MOVE : DragBehavior.COPY;
      event.setResult(DragResult.DRAG_SUCCESSFUL);
    } catch (_error) {
      this.status = 'Cannot receive this data';
      event.setResult(DragResult.DRAG_FAILED);
    }
  }

  private report(event: DragEvent): void {
    this.status = 'Source result ' + event.getResult() + ', effect ' + event.dragBehavior;
  }

  @Builder
  private preview() {
    Text('Drag fixture').padding(12).backgroundColor(0xDCEBFE)
  }

  build() {
    Column({ space: 12 }) {
      Text('HarmonyOS drag peer').fontSize(22)
      TextInput({ placeholder: 'Drop plain text here' })
      Row({ space: 8 }) {
        Button('Prepare fixtures').onClick(() => this.prepareFiles())
        Button('Read fixtures').onClick(() => {
          if (this.readOnlyPath && this.writablePath) {
            this.status = 'Read only: ' + fileIo.readTextSync(this.readOnlyPath) +
              '\nRead/write: ' + fileIo.readTextSync(this.writablePath);
          }
        })
      }
      Text('Drag plain text, HTML and link').padding(12).backgroundColor(0xDCEBFE)
        .draggable(true).onDragStart((event: DragEvent): CustomBuilder => {
          this.startRichDrag(event);
          return (): void => this.preview();
        }).onDragEnd((event: DragEvent): void => this.report(event))
      Text('Drag read-only file').padding(12).backgroundColor(0xDCEBFE)
        .draggable(true).onDragStart((event: DragEvent): CustomBuilder => {
          this.startFileDrag(event, false);
          return (): void => this.preview();
        }).onDragEnd((event: DragEvent): void => this.report(event))
      Text('Drag writable file').padding(12).backgroundColor(0xDCEBFE)
        .draggable(true).onDragStart((event: DragEvent): CustomBuilder => {
          this.startFileDrag(event, true);
          return (): void => this.preview();
        }).onDragEnd((event: DragEvent): void => this.report(event))
      Text('Drop Counter data here').width('100%').height(64).backgroundColor(0xE0F6E0)
        .textAlign(TextAlign.Center)
        .allowDrop([
          uniformTypeDescriptor.UniformDataType.PLAIN_TEXT, uniformTypeDescriptor.UniformDataType.HTML,
          uniformTypeDescriptor.UniformDataType.HYPERLINK, uniformTypeDescriptor.UniformDataType.FILE,
          uniformTypeDescriptor.UniformDataType.FILE_URI
        ])
        .onDragMove((event: DragEvent): void => {
          const summary = event.getSummary().summary;
          const file = summary[uniformTypeDescriptor.UniformDataType.FILE_URI] !== undefined ||
            summary[uniformTypeDescriptor.UniformDataType.FILE] !== undefined;
          event.dragBehavior = event.getModifierKeyState?.(['Shift']) && !file ? DragBehavior.MOVE : DragBehavior.COPY;
          event.setResult(DragResult.DROP_ENABLED);
        })
        .onDrop((event: DragEvent): void => this.receive(event))
      Text(this.status).width('100%')
    }.padding(16).width('100%').height('100%')
  }
}
