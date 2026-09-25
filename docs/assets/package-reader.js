/**
 * NEXUS Package Reader & Validator
 * Reads .nupkg / .nexuspkg / .zip update archives directly in the browser
 * with zero external dependencies using native Web APIs and DecompressionStream.
 */
(function (global) {
  'use strict';

  function bufferToHex(buffer) {
    const bytes = new Uint8Array(buffer);
    let hex = '';
    for (let i = 0; i < bytes.length; i++) {
      hex += bytes[i].toString(16).padStart(2, '0');
    }
    return hex;
  }

  async function computeSha256(arrayBuffer) {
    if (crypto && crypto.subtle && crypto.subtle.digest) {
      const hashBuf = await crypto.subtle.digest('SHA-256', arrayBuffer);
      return bufferToHex(hashBuf);
    }
    return '';
  }

  // Dotted version comparison: returns >0 if v1 > v2, <0 if v1 < v2, 0 if equal
  function compareVersions(v1, v2) {
    if (!v1 || !v2) return 0;
    const clean1 = (v1 + '').replace(/^[^\d]*/, '').split('.').map(n => parseInt(n, 10) || 0);
    const clean2 = (v2 + '').replace(/^[^\d]*/, '').split('.').map(n => parseInt(n, 10) || 0);
    const maxLen = Math.max(clean1.length, clean2.length);
    for (let i = 0; i < maxLen; i++) {
      const p1 = clean1[i] || 0;
      const p2 = clean2[i] || 0;
      if (p1 > p2) return 1;
      if (p1 < p2) return -1;
    }
    return 0;
  }

  async function decompressDeflateRaw(compressedBytes) {
    if (typeof DecompressionStream !== 'undefined') {
      try {
        const ds = new DecompressionStream('deflate-raw');
        const writer = ds.writable.getWriter();
        writer.write(compressedBytes);
        writer.close();
        const response = new Response(ds.readable);
        const arrayBuf = await response.arrayBuffer();
        return new Uint8Array(arrayBuf);
      } catch (e) {
        console.warn('DecompressionStream deflate-raw failed, trying inflate:', e);
      }
    }
    // Fallback if uncompressed or stream fails
    return compressedBytes;
  }

  class NexusPackageReader {
    /**
     * Inspects and validates an update file (.nupkg, .nexuspkg, .zip)
     * @param {File|Blob} file 
     * @param {string} currentInstalledVersion
     * @returns {Promise<{valid: boolean, error?: string, version?: string, metadata?: any, sha256?: string, fileCount?: number, size?: number, files?: Array}>}
     */
    static async inspectPackage(file, currentInstalledVersion = '8.0.2') {
      if (!file) {
        return { valid: false, error: 'Файл обновления не выбран.' };
      }

      const fileName = file.name.toLowerCase();
      const validExts = ['.nupkg', '.nexuspkg', '.zip'];
      const hasValidExt = validExts.some(ext => fileName.endsWith(ext));

      if (!hasValidExt) {
        return {
          valid: false,
          error: 'Этот файл не является корректным обновлением NEXUS Builder.'
        };
      }

      let arrayBuffer;
      try {
        arrayBuffer = await file.arrayBuffer();
      } catch (e) {
        return { valid: false, error: 'Не удалось прочитать файл обновления.' };
      }

      if (arrayBuffer.byteLength < 22) {
        return { valid: false, error: 'Этот файл не является корректным обновлением NEXUS Builder.' };
      }

      // 1. Calculate SHA-256 integrity hash
      let packageHash = '';
      try {
        packageHash = await computeSha256(arrayBuffer);
      } catch (e) {
        console.error('SHA-256 calc failed:', e);
      }

      // 2. Parse ZIP Central Directory
      const view = new DataView(arrayBuffer);
      const uint8 = new Uint8Array(arrayBuffer);
      let eocdOffset = -1;

      // Find End of Central Directory signature (0x06054b50)
      for (let i = uint8.length - 22; i >= Math.max(0, uint8.length - 65557); i--) {
        if (view.getUint32(i, true) === 0x06054b50) {
          eocdOffset = i;
          break;
        }
      }

      if (eocdOffset === -1) {
        return {
          valid: false,
          error: 'Этот файл не является корректным обновлением NEXUS Builder.'
        };
      }

      const cdEntriesCount = view.getUint16(eocdOffset + 10, true);
      const cdOffset = view.getUint32(eocdOffset + 16, true);

      let currentOffset = cdOffset;
      const fileList = [];
      let metadataJsonText = null;

      for (let idx = 0; idx < cdEntriesCount; idx++) {
        if (currentOffset + 46 > uint8.length) break;
        const sig = view.getUint32(currentOffset, true);
        if (sig !== 0x02014b50) break; // Central Directory Header signature

        const compressionMethod = view.getUint16(currentOffset + 10, true);
        const compSize = view.getUint32(currentOffset + 20, true);
        const uncompSize = view.getUint32(currentOffset + 24, true);
        const nameLen = view.getUint16(currentOffset + 28, true);
        const extraLen = view.getUint16(currentOffset + 30, true);
        const commentLen = view.getUint16(currentOffset + 32, true);
        const localHeaderOffset = view.getUint32(currentOffset + 42, true);

        const nameBytes = uint8.subarray(currentOffset + 46, currentOffset + 46 + nameLen);
        const entryName = new TextDecoder('utf-8').decode(nameBytes);

        fileList.push({
          path: entryName,
          compressedSize: compSize,
          size: uncompSize,
          isDir: entryName.endsWith('/')
        });

        // Check if this is metadata.json
        if (entryName === 'metadata.json' || entryName.endsWith('/metadata.json')) {
          try {
            const locSig = view.getUint32(localHeaderOffset, true);
            if (locSig === 0x04034b50) {
              const locNameLen = view.getUint16(localHeaderOffset + 26, true);
              const locExtraLen = view.getUint16(localHeaderOffset + 28, true);
              const dataStart = localHeaderOffset + 30 + locNameLen + locExtraLen;
              const rawData = uint8.subarray(dataStart, dataStart + compSize);

              let extractedBytes;
              if (compressionMethod === 0) { // Uncompressed
                extractedBytes = rawData;
              } else if (compressionMethod === 8) { // Deflate
                extractedBytes = await decompressDeflateRaw(rawData);
              } else {
                extractedBytes = rawData;
              }

              if (extractedBytes && extractedBytes.length > 0) {
                metadataJsonText = new TextDecoder('utf-8').decode(extractedBytes);
              }
            }
          } catch (e) {
            console.error('Failed to extract metadata.json:', e);
          }
        }

        currentOffset += 46 + nameLen + extraLen + commentLen;
      }

      if (!metadataJsonText) {
        // Look for version pattern in package name as fallback
        const match = fileName.match(/update[_-]([0-9]+(?:\.[0-9]+)*)/i);
        if (!match) {
          return {
            valid: false,
            error: 'Этот файл не является корректным обновлением NEXUS Builder.'
          };
        }
        metadataJsonText = JSON.stringify({
          version: match[1],
          packageType: 'NEXUS_UPDATE_PACKAGE',
          description: 'Пакет обновления NEXUS'
        });
      }

      let metadata;
      try {
        metadata = JSON.parse(metadataJsonText);
      } catch (e) {
        return {
          valid: false,
          error: 'Не удалось проверить файл обновления.'
        };
      }

      const targetVersion = metadata.version || metadata.targetVersion;
      if (!targetVersion) {
        return {
          valid: false,
          error: 'Этот файл не является корректным обновлением NEXUS Builder.'
        };
      }

      // 3. Check version vs installed version
      if (currentInstalledVersion && compareVersions(targetVersion, currentInstalledVersion) <= 0) {
        return {
          valid: false,
          error: 'Выбранная версия старее установленной.',
          version: targetVersion,
          currentVersion: currentInstalledVersion
        };
      }

      return {
        valid: true,
        version: targetVersion,
        metadata: metadata,
        sha256: packageHash,
        fileCount: fileList.length,
        size: arrayBuffer.byteLength,
        files: fileList,
        fileName: file.name
      };
    }
  }

  global.NexusPackageReader = NexusPackageReader;
  global.compareVersions = compareVersions;
})(typeof window !== 'undefined' ? window : this);
