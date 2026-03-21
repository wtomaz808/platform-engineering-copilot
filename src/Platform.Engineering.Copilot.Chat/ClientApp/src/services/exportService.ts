import { jsPDF } from 'jspdf';
import PptxGenJS from 'pptxgenjs';
import { ChatMessage, MessageRole } from '../types/chat';

/** Strip markdown syntax for plain-text export */
function stripMarkdown(md: string): string {
  return md
    .replace(/```[\s\S]*?```/g, (m) => m.replace(/```\w*\n?/g, '').trim())
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/\*(.+?)\*/g, '$1')
    .replace(/#{1,6}\s+/g, '')
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
    .replace(/^\s*[-*+]\s+/gm, '  - ')
    .replace(/^\s*\d+\.\s+/gm, (m) => `  ${m.trim()} `)
    .replace(/`([^`]+)`/g, '$1');
}

/** Word-wrap long lines to fit within a given character width */
function wrapText(text: string, maxChars: number): string[] {
  const lines: string[] = [];
  for (const raw of text.split('\n')) {
    if (raw.length <= maxChars) {
      lines.push(raw);
    } else {
      let remaining = raw;
      while (remaining.length > maxChars) {
        let breakAt = remaining.lastIndexOf(' ', maxChars);
        if (breakAt <= 0) breakAt = maxChars;
        lines.push(remaining.substring(0, breakAt));
        remaining = remaining.substring(breakAt).trimStart();
      }
      if (remaining) lines.push(remaining);
    }
  }
  return lines;
}

export function exportToPdf(messages: ChatMessage[], title: string) {
  const doc = new jsPDF({ unit: 'mm', format: 'a4' });
  const pageW = doc.internal.pageSize.getWidth();
  const pageH = doc.internal.pageSize.getHeight();
  const margin = 15;
  const usable = pageW - margin * 2;
  const lineHeight = 5;
  let y = margin;

  const ensureSpace = (needed: number) => {
    if (y + needed > pageH - margin) {
      doc.addPage();
      y = margin;
    }
  };

  // Title
  doc.setFontSize(16);
  doc.setFont('helvetica', 'bold');
  doc.text(title || 'PE Copilot Conversation', margin, y);
  y += 10;

  doc.setFontSize(9);
  doc.setFont('helvetica', 'normal');
  doc.text(`Exported: ${new Date().toLocaleString()}`, margin, y);
  y += 8;

  // Separator line
  doc.setDrawColor(200);
  doc.line(margin, y, pageW - margin, y);
  y += 5;

  for (const msg of messages) {
    const role = msg.role === MessageRole.User ? 'You' : 'PE Copilot';
    const time = new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

    // Role header
    ensureSpace(lineHeight * 2);
    doc.setFontSize(10);
    doc.setFont('helvetica', 'bold');
    doc.setTextColor(msg.role === MessageRole.User ? 37 : 30, msg.role === MessageRole.User ? 99 : 64, msg.role === MessageRole.User ? 235 : 175);
    doc.text(`${role}  [${time}]`, margin, y);
    y += lineHeight + 1;

    // Content
    doc.setFont('helvetica', 'normal');
    doc.setTextColor(30, 30, 30);
    doc.setFontSize(9);
    const plain = stripMarkdown(msg.content);
    const maxChars = Math.floor(usable / 2); // rough estimate for 9pt
    const lines = wrapText(plain, maxChars);

    for (const line of lines) {
      ensureSpace(lineHeight);
      doc.text(line, margin, y);
      y += lineHeight;
    }

    y += 3;

    // Separator
    ensureSpace(3);
    doc.setDrawColor(230);
    doc.line(margin, y, pageW - margin, y);
    y += 4;
  }

  doc.save(`${(title || 'conversation').replace(/[^a-zA-Z0-9]/g, '_')}.pdf`);
}

export function exportToPptx(messages: ChatMessage[], title: string) {
  const pptx = new PptxGenJS();
  pptx.title = title || 'PE Copilot Conversation';
  pptx.author = 'Platform Engineering Copilot';

  // Title slide
  const titleSlide = pptx.addSlide();
  titleSlide.addText(title || 'PE Copilot Conversation', {
    x: 0.5, y: 1.5, w: 9, h: 1.5,
    fontSize: 28, bold: true, color: '1E40AF',
    align: 'center',
  });
  titleSlide.addText(`Exported: ${new Date().toLocaleString()}`, {
    x: 0.5, y: 3.2, w: 9, h: 0.5,
    fontSize: 12, color: '6B7280',
    align: 'center',
  });
  titleSlide.addText('Platform Engineering Copilot', {
    x: 0.5, y: 4, w: 9, h: 0.5,
    fontSize: 14, color: '3B82F6',
    align: 'center',
  });

  // Chunk messages into slides (roughly 3-4 per slide depending on length)
  const maxSlideChars = 1200;
  let currentSlide: PptxGenJS.Slide | null = null;
  let slideText: Array<{ text: string; options: PptxGenJS.TextPropsOptions }> = [];
  let slideChars = 0;

  const flushSlide = () => {
    if (slideText.length && currentSlide) {
      currentSlide.addText(slideText, {
        x: 0.4, y: 0.4, w: 9.2, h: 6.6,
        valign: 'top',
        shrinkText: true,
      });
    }
    slideText = [];
    slideChars = 0;
  };

  for (const msg of messages) {
    const role = msg.role === MessageRole.User ? 'You' : 'PE Copilot';
    const time = new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    const plain = stripMarkdown(msg.content);
    const header = `${role}  [${time}]\n`;
    const body = `${plain}\n\n`;
    const needed = header.length + body.length;

    if (!currentSlide || slideChars + needed > maxSlideChars) {
      flushSlide();
      currentSlide = pptx.addSlide();
    }

    slideText.push({
      text: header,
      options: {
        fontSize: 11,
        bold: true,
        color: msg.role === MessageRole.User ? '2563EB' : '1E40AF',
      },
    });
    slideText.push({
      text: body,
      options: {
        fontSize: 9,
        color: '374151',
      },
    });
    slideChars += needed;
  }
  flushSlide();

  pptx.writeFile({ fileName: `${(title || 'conversation').replace(/[^a-zA-Z0-9]/g, '_')}.pptx` });
}
