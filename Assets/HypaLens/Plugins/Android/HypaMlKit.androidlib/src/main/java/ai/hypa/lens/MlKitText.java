package ai.hypa.lens;

import android.graphics.Bitmap;
import android.graphics.Rect;

import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.Text;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.nio.ByteBuffer;
import java.util.concurrent.TimeUnit;

/**
 * Hypa Lens bridge to Google ML Kit on-device text recognition. Called from C# (HypaLens.Quest.MlKitRecognizer)
 * on a worker thread, never the UI thread. Returns lines and words with boxes in image pixels (top-left origin).
 * The image is held in memory only for the call.
 */
public final class MlKitText {
    private static TextRecognizer recognizer;

    private MlKitText() {}

    public static boolean isAvailable() {
        try {
            Class.forName("com.google.mlkit.vision.text.TextRecognition");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    private static synchronized TextRecognizer get() {
        if (recognizer == null) recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
        return recognizer;
    }

    /**
     * @param rgba   width*height*4 bytes, top row first, R G B A per pixel
     * @return JSON: {"lines":[{"text","x","y","w","h","words":[{"text","x","y","w","h"}]}]}
     */
    public static String recognize(byte[] rgba, int width, int height) throws Exception {
        Bitmap bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
        try {
            bmp.copyPixelsFromBuffer(ByteBuffer.wrap(rgba));
            Text text = Tasks.await(get().process(InputImage.fromBitmap(bmp, 0)), 10, TimeUnit.SECONDS);
            JSONArray lines = new JSONArray();
            for (Text.TextBlock block : text.getTextBlocks()) {
                for (Text.Line line : block.getLines()) {
                    JSONObject jl = box(line.getText(), line.getBoundingBox());
                    if (jl == null) continue;
                    JSONArray words = new JSONArray();
                    for (Text.Element e : line.getElements()) {
                        JSONObject jw = box(e.getText(), e.getBoundingBox());
                        if (jw != null) words.put(jw);
                    }
                    jl.put("words", words);
                    lines.put(jl);
                }
            }
            return new JSONObject().put("lines", lines).toString();
        } finally {
            bmp.recycle();
        }
    }

    private static JSONObject box(String t, Rect r) throws JSONException {
        if (t == null || r == null) return null;
        return new JSONObject().put("text", t).put("x", r.left).put("y", r.top).put("w", r.width()).put("h", r.height());
    }
}
