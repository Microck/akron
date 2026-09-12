# Portable snapshot bundle

The setup v10 transport keeps each reconstruction document byte-for-byte intact.
It does not parse and rewrite JSON numbers, strings, object order, or graph nodes.
Local saves use gzip with `akron-reconstruction-v11`. Recapture older StartPos
slots, then re-export their setup packs.

## Wire contract

An archive with snapshots contains one stored ZIP entry,
`startpos/snapshots.bin.br`. Its SHA-256 covers the complete compressed entry.
Each slot's snapshot SHA-256 covers its original, uncompressed JSON bytes.

The entry is a single Brotli stream, encoded at quality 11 with window 24.
There must be no trailing bytes or concatenated streams. After Brotli decoding,
read frames until EOF. Each frame starts with a one-byte tag and a little-endian
unsigned 32-bit payload length:

- Tag 0: 1 through 65,536 literal bytes.
- Tag 1: 3 through 49,152 binary bytes, with length divisible by three. Expand
  them to standard, unpadded ASCII base64 and append those bytes.

Frame parsing starts with 65,536 units of fragmentation credit. Before reading
each payload, update `credit = min(65536, credit + decodedFrameBytes - 64)`;
reject the frame if the result is negative. For tag 1, `decodedFrameBytes` is
four thirds of the binary payload length. The credit cap prevents large
earlier frames from funding an arbitrarily long burst of tiny frames.

The resulting byte stream has this structure. All integers are unsigned 32-bit
little-endian values:

1. Eight literal magic bytes: `AKRSB001`.
2. Dictionary count, at most 4,096.
3. For each dictionary item: byte length, then bytes. Each item contains 1
   through 65,536 bytes. The total dictionary is at most 16 MiB.
4. Document count, from 1 through 99.
5. For each document: slot number, original byte length, then commands until
   exactly that many bytes have been reconstructed. Slots are strictly
   increasing, from 1 through 99. Each document contains 1 through 384 MiB.
   The combined expanded length of all documents cannot exceed 1 GiB.
6. EOF, with no additional frames or bytes.

A document command starts with one byte. Tag 0 is followed by a literal length
and 1 through 65,536 literal bytes. Tag 1 is followed by a zero-based dictionary
index. Neither command may exceed the document's remaining length.
Each document permits at most `1024 + ceil(originalByteLength / 4096)`
commands. Together with the frame-credit limit, this bounds parsing work
even when the compressed entry and reconstructed documents are small.

## Encoder policy

The decoder does not require the encoder's chunk boundaries. Akron's
encoder uses content-defined chunks with a 4 KiB minimum, 16 KiB target, and
64 KiB maximum. It keeps at most 65,536 discovery records and spools at most
128 MiB of candidate bytes. The dictionary itself remains capped at 16 MiB.
Repeated chunks are ranked by estimated compressed bytes saved per dictionary
byte, not by their raw length alone.

The encoder writes two candidates when a useful dictionary exists: one with
that dictionary and one with an empty dictionary. It keeps the smaller complete
Brotli file. These are encoder choices within one format, not compatibility
paths. Input gzip checksums and raw document checksums detect source changes
between capture and encoding. Cancellation removes temporary files.

The base64 transform recognizes runs of the standard ASCII base64 alphabet.
A run is packed only after it reaches 128 bytes.
It converts only complete four-character groups, leaving padding, short tails,
quotes, escapes, and all other bytes literal. Thus it is reversible for arbitrary
input bytes and does not depend on JSON string semantics.

## Verification still required

The public upload service accepts at most 32 MiB per `.akr` and 384 MiB of
expanded snapshot JSON across the entire pack. This is a service resource limit,
not the local format's per-document limit. It also bounds JSON tokens to 16 MiB,
nesting to 128 levels, and object keys to 4,096 per object. Larger local packs
must be split before upload; the service does not silently discard slots.

Prototype measurements favor shared chunks and the reversible base64 transform
over plain Brotli, zstd, and xz on the sampled saves. Production codec round trips,
matched quality-11 controls, cross-language decoding, archive integration, and
Cloudflare runtime measurements must pass before this contract is released.
