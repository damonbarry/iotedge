// Example:
//
//     cargo run --example subscriber -- --server 127.0.0.1:1883 --client-id 'example-subscriber' --topic-filter foo --qos 1

#![allow(clippy::let_unit_value)]

mod common;

use std::convert::TryInto;
use std::str::FromStr;

use opentelemetry::{
    api::{
        context::Context, Key, SpanContext, SpanId, TraceContextExt, TraceId, Tracer,
        TRACE_FLAG_SAMPLED,
    },
    global, sdk,
};

#[derive(Debug, structopt::StructOpt)]
struct Options {
    #[structopt(help = "Address of the MQTT server.", long = "server")]
    server: std::net::SocketAddr,

    #[structopt(
        help = "Client ID used to identify this application to the server. If not given, a server-generated ID will be used.",
        long = "client-id"
    )]
    client_id: Option<String>,

    #[structopt(
        help = "Username used to authenticate with the server, if any.",
        long = "username"
    )]
    username: Option<String>,

    #[structopt(
        help = "Password used to authenticate with the server, if any.",
        long = "password"
    )]
    password: Option<String>,

    #[structopt(
		help = "Maximum back-off time between reconnections to the server, in seconds.",
		long = "max-reconnect-back-off",
		default_value = "30",
		parse(try_from_str = common::duration_from_secs_str),
	)]
    max_reconnect_back_off: std::time::Duration,

    #[structopt(
		help = "Keep-alive time advertised to the server, in seconds.",
		long = "keep-alive",
		default_value = "5",
		parse(try_from_str = common::duration_from_secs_str),
	)]
    keep_alive: std::time::Duration,

    #[structopt(help = "The topic filter to subscribe to.", long = "topic-filter")]
    topic_filter: String,

    #[structopt(help = "The QoS with which to subscribe to the topic.", long = "qos", parse(try_from_str = common::qos_from_str))]
    qos: mqtt3::proto::QoS,
}

fn init_tracer() -> thrift::Result<()> {
    let exporter = opentelemetry_jaeger::Exporter::builder()
        .with_agent_endpoint("127.0.0.1:6831".parse().unwrap())
        .with_process(opentelemetry_jaeger::Process {
            service_name: "mqtt_subscriber".to_string(),
            tags: vec![],
        })
        .init()?;
    let provider = sdk::Provider::builder()
        .with_simple_exporter(exporter)
        .with_config(sdk::Config {
            default_sampler: Box::new(sdk::Sampler::AlwaysOn),
            ..Default::default()
        })
        .build();
    global::set_provider(provider);

    Ok(())
}

fn main() {
    env_logger::Builder::from_env(env_logger::Env::new().filter_or(
        "MQTT3_LOG",
        "mqtt3=debug,mqtt3::logging=trace,subscriber=info",
    ))
    .init();

    init_tracer().expect("couldn't initialize tracer");
    let tracer = global::tracer("subscriber-main");

    let Options {
        server,
        client_id,
        username,
        password,
        max_reconnect_back_off,
        keep_alive,
        topic_filter,
        qos,
    } = structopt::StructOpt::from_args();

    let topic_filter = if !topic_filter.ends_with("/+") && !topic_filter.ends_with("/#") {
        format!("{}/+", topic_filter)
    } else {
        topic_filter
    };

    let mut runtime = tokio::runtime::Runtime::new().expect("couldn't initialize tokio runtime");

    let client = mqtt3::Client::new(
        client_id,
        username,
        None,
        move || {
            let password = password.clone();
            Box::pin(async move {
                let io = tokio::net::TcpStream::connect(&server).await;
                io.map(|io| (io, password))
            })
        },
        max_reconnect_back_off,
        keep_alive,
    );

    let mut shutdown_handle = client
        .shutdown_handle()
        .expect("couldn't get shutdown handle");
    runtime.spawn(async move {
        let () = tokio::signal::ctrl_c()
            .await
            .expect("couldn't get Ctrl-C notification");
        let result = shutdown_handle.shutdown().await;
        let () = result.expect("couldn't send shutdown notification");
    });

    let mut update_subscription_handle = client
        .update_subscription_handle()
        .expect("couldn't get subscription update handle");
    runtime.spawn(async move {
        let result = update_subscription_handle
            .subscribe(mqtt3::proto::SubscribeTo { topic_filter, qos })
            .await;
        if let Err(err) = result {
            panic!("couldn't update subscription: {}", err);
        }
    });

    runtime.block_on(async {
        use futures_util::StreamExt;

        let mut client = client.enumerate();

        while let Some((i, event)) = client.next().await {
            let event = event.unwrap();

            if let mqtt3::Event::Publication(publication) = event {
                let segments = publication
                    .topic_name
                    .parse::<Topic>()
                    .expect("couldn't parse received publication's topic")
                    .segments;

                let traceparent = segments.last().expect("received publication's topic doesn't contain any segments");

                if let Ok(Some(span_context)) = extract_span_context(traceparent) {
                    let _attach = Context::current().with_remote_span_context(span_context);
                    tracer.in_span("subscriber_receive", |context| {
                        let span = context.span();
                        span.set_attribute(Key::from("iteration").u64(i.try_into().unwrap()));
                        match std::str::from_utf8(&publication.payload) {
                            Ok(s) => {
                                log::info!(
                                    "Received publication: {:?} {:?} {:?}",
                                    publication.topic_name,
                                    s,
                                    publication.qos,
                                );
                                span.add_event(format!(
                                    "Received publication: {:?} {:?} {:?}",
                                    publication.topic_name,
                                    s,
                                    publication.qos
                                ), vec![])
                            },
                            Err(_) => {
                                log::info!(
                                    "Received publication: {:?} {:?} {:?}",
                                    publication.topic_name,
                                    publication.payload,
                                    publication.qos,
                                );
                                span.add_event(format!(
                                    "(Failed to convert bytes to UTF-8) Received publication: {:?} {:?} {:?}",
                                    publication.topic_name,
                                    publication.payload,
                                    publication.qos
                                ), vec![])
                            },
                        }
                    });
                }
            }
        }
    });
}

const NUL_CHAR: char = '\0';
const TOPIC_SEPARATOR: char = '/';

struct Topic {
    segments: Vec<String>,
}

impl FromStr for Topic {
    type Err = ();

    fn from_str(string: &str) -> Result<Self, Self::Err> {
        // [MQTT-4.7.3-1] - All Topic Names and Topic Filters MUST be at least
        // one character long.
        // [MQTT-4.7.3-2] - Topic Names and Topic Filters MUST NOT include the
        // null character (Unicode U+0000).
        if string.is_empty() || string.contains(NUL_CHAR) {
            return Err(());
        }

        let mut segments = Vec::new();
        for s in string.split(TOPIC_SEPARATOR) {
            segments.push(s.to_owned());
        }

        let topic = Topic { segments };
        Ok(topic)
    }
}

static MAX_VERSION: u8 = 254;

fn extract_span_context(string: &str) -> Result<Option<SpanContext>, ()> {
    let values = string.splitn(2, "=").collect::<Vec<&str>>();
    if values.len() < 2 || values[0] != "traceparent" {
        return Ok(None);
    }

    let parts = values[1].split_terminator('-').collect::<Vec<&str>>();
    // Ensure parts are not out of range.
    if parts.len() < 4 {
        return Err(());
    }

    // Ensure version is within range, for version 0 there must be 4 parts.
    let version = u8::from_str_radix(parts[0], 16).map_err(|_| ())?;
    if version > MAX_VERSION || version == 0 && parts.len() != 4 {
        return Err(());
    }

    // Parse trace id section
    let trace_id = u128::from_str_radix(parts[1], 16).map_err(|_| ())?;

    // Parse span id section
    let span_id = u64::from_str_radix(parts[2], 16).map_err(|_| ())?;

    // Parse trace flags section
    let opts = u8::from_str_radix(parts[3], 16).map_err(|_| ())?;

    // Ensure opts are valid for version 0
    if version == 0 && opts > 2 {
        return Err(());
    }
    // Build trace flags
    let trace_flags = opts & TRACE_FLAG_SAMPLED;

    // create context
    let span_context = SpanContext::new(
        TraceId::from_u128(trace_id),
        SpanId::from_u64(span_id),
        trace_flags,
        true,
    );

    // Ensure span is valid
    if !span_context.is_valid() {
        return Err(());
    }

    Ok(Some(span_context))
}
